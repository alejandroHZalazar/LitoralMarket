using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Helpers;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LitoralMarket.Infrastructure.Services;

public class PedidoService : IPedidoService
{
    private readonly AppDbContext       _db;
    private readonly IEmailService      _email;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDbContextFactory<AppDbContext> _ctxFactory;
    private readonly IVentaEcommerceService _ventas;
    private readonly ILogger<PedidoService> _logger;
    private readonly IMemoryCache _cache;
    private readonly IPromocionesService _promos;

    public PedidoService(
        AppDbContext                    db,
        IEmailService                   email,
        IServiceScopeFactory            scopeFactory,
        IDbContextFactory<AppDbContext> ctxFactory,
        IVentaEcommerceService          ventas,
        ILogger<PedidoService>          logger,
        IMemoryCache                    cache,
        IPromocionesService             promos)
    {
        _promos       = promos;
        _cache        = cache;
        _db           = db;
        _email        = email;
        _scopeFactory = scopeFactory;
        _ctxFactory   = ctxFactory;
        _ventas       = ventas;
        _logger       = logger;
    }

    public async Task<(bool valido, List<string> errores)> ValidarStockAsync(int pedidoId)
    {
        var errores = new List<string>();

        // Contexto propio de la factory: es una lectura pura, así puede ejecutarse
        // en paralelo con otras lecturas independientes (el DbContext scoped no
        // admite consultas concurrentes).
        await using var db = await _ctxFactory.CreateDbContextAsync();

        // Límite por stock (ecommerce/LimitarCantidadPorStock = '1'). Se lee con el contexto PROPIO de la
        // factory y la misma caché de parámetros: este método corre en paralelo con ActualizarPreciosAsync,
        // que usa el contexto scoped y no admite consultas concurrentes.
        var limitar = await new ParametrosService(db, _cache).LimitarCantidadPorStockAsync();

        // Proyección directa: no materializa la entidad Producto ni carga el blob
        // 'imagen'. Solo trae cantidad solicitada, stock disponible y cantidad
        // mínima de venta por línea.
        var detalles = await db.PedidoDetalles
            .Where(d => d.FkPedido == pedidoId)
            .Select(d => new
            {
                d.FkProducto,
                d.Descripcion,
                d.Cantidad,
                Stock = d.Producto != null && d.Producto.Stock != null
                        ? d.Producto.Stock.Cantidad
                        : (decimal?)0,
                CantidadMinimaVenta = d.Producto != null ? d.Producto.CantidadMinimaVenta : null,
                Fraccionado = d.Producto != null && d.Producto.Fraccionado == true
            })
            .ToListAsync();

        // Un producto con promocion por cantidad (NxM) queda en el pedido confirmado como DOS lineas (cobradas y
        // bonificadas): las reglas de stock y de cantidad minima valen para el total del producto, no por linea.
        // Sin promociones cada producto aparece una sola vez, asi que esto no cambia nada.
        detalles = detalles
            .GroupBy(d => d.FkProducto.HasValue ? (object)d.FkProducto.Value : new object())
            .Select(g => new
            {
                g.First().FkProducto, g.First().Descripcion,
                Cantidad = (decimal?)g.Sum(x => x.Cantidad ?? 0m),
                g.First().Stock, g.First().CantidadMinimaVenta, g.First().Fraccionado
            })
            .ToList();

        foreach (var d in detalles)
        {
            var stock = d.Stock ?? 0;
            var cantidadPedida = d.Cantidad ?? 0;

            // Restricción de stock: solo bloquea si no queda stock (stock <= 0). Si hay
            // stock (> 0) se permite pedir cualquier cantidad, sin comparar contra el stock.
            if (stock <= 0 && cantidadPedida > 0)
                errores.Add($"'{d.Descripcion}': sin stock disponible");

            // Con LimitarCantidadPorStock = '1': la cantidad pedida no puede superar el stock disponible.
            // (Con stock <= 0 ya se informó arriba "sin stock disponible".)
            if (limitar && stock > 0 && cantidadPedida > 0)
            {
                var maximo = CantidadVentaHelper.MaximoPermitido(stock, d.CantidadMinimaVenta, d.Fraccionado);
                var unidad = CantidadVentaHelper.UnidadMinima(d.CantidadMinimaVenta, d.Fraccionado);

                if (maximo < unidad)
                    errores.Add($"'{d.Descripcion}': hay {CantidadVentaHelper.FormatearStock(stock)} disponibles, menos que la cantidad mínima de venta ({CantidadVentaHelper.Formatear(unidad)})");
                else if (cantidadPedida > maximo)
                    errores.Add($"'{d.Descripcion}': solicitaste {CantidadVentaHelper.Formatear(cantidadPedida)}, hay {CantidadVentaHelper.FormatearStock(stock)} disponibles (máximo {CantidadVentaHelper.Formatear(maximo)})");
            }

            // Re-chequeo de cantidadMinimaVenta: defensa server-side final por si el
            // request al confirmar el pedido fue manipulado saltando la validación
            // del carrito/frontend.
            if (CantidadVentaHelper.RequiereMultiplo(d.CantidadMinimaVenta) &&
                !CantidadVentaHelper.EsCantidadValida(d.Cantidad ?? 0, d.CantidadMinimaVenta))
                errores.Add($"'{d.Descripcion}': se vende en múltiplos de {d.CantidadMinimaVenta}, cantidad solicitada {d.Cantidad:N2}");
        }

        return (!errores.Any(), errores);
    }

    /// <inheritdoc />
    public async Task<int> PrepararPagoAsync(int pedidoId, CheckoutDto datos)
    {
        // Sin Include(Detalles): este método no lee las líneas del pedido, solo
        // actualiza la cabecera (cliente, dirección, total, estado). Cargarlas era
        // trabajo muerto que traía todas las filas de detalle sin usarlas.
        var pedido = await _db.Pedidos
            .FirstOrDefaultAsync(p => p.Id == pedidoId);

        if (pedido is null)
            throw new InvalidOperationException("Pedido no encontrado");

        if (pedido.EstadoEcommerce != "borrador")
            throw new InvalidOperationException($"El pedido no está en estado borrador (estado actual: {pedido.EstadoEcommerce})");

        // Promociones por cantidad / porcentaje (promociones/descuentos = 1): se fijan en el pedido ahora que deja de ser
        // borrador (el servidor recalcula; nada de lo que haya enviado el navegador cuenta). Deja el total de productos al
        // dia, antes de sumar el envio. Apagado: no hace nada.
        await _promos.MaterializarPedidoAsync(pedidoId);

        // Datos del cliente
        if (datos.ClienteId.HasValue)
        {
            pedido.FkCliente = datos.ClienteId;
        }
        else
        {
            pedido.NombreCliente    = datos.NombreCliente;
            pedido.EmailCliente     = datos.EmailCliente;
            pedido.TelefonoCliente  = datos.TelefonoCliente;
        }

        // Dirección de entrega y costo de envío
        var direccion = await _db.DireccionesEntrega.FindAsync(datos.DireccionEntregaId);
        if (direccion is not null)
        {
            pedido.FkDireccionEntrega = direccion.Id;
            pedido.CostoEnvio         = direccion.CostoEnvio;

            var textoDir = direccion.Descripcion;
            if (direccion.PermiteLibre && !string.IsNullOrWhiteSpace(datos.DireccionEntregaTexto))
            {
                pedido.DireccionEntregaTexto = datos.DireccionEntregaTexto;
                textoDir += $": {datos.DireccionEntregaTexto}";
            }
            else if (!string.IsNullOrWhiteSpace(direccion.Direccion))
            {
                textoDir += $" — {direccion.Direccion}";
                if (!string.IsNullOrWhiteSpace(direccion.Localidad))
                    textoDir += $", {direccion.Localidad}";
            }
            pedido.DireccionEntrega = textoDir;

            // Sumar costo de envío al total
            pedido.Total = (pedido.Total ?? 0) + direccion.CostoEnvio;
        }

        pedido.Observacion      = datos.Observacion;
        pedido.EstadoEcommerce  = "pendiente_pago";
        pedido.Fecha            = DateTime.Now;

        await _db.SaveChangesAsync();
        return pedido.Id;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Flujo de PAGO (webhook MercadoPago, reembolso): confirma el pedido y
    /// descuenta stock + registra el movimiento de egreso.
    /// </remarks>
    public Task ConfirmarPagoAsync(int pedidoId) =>
        ConfirmarInternoAsync(pedidoId, descontarStock: true);

    /// <summary>
    /// Transición pendiente_pago → confirmado. Con <paramref name="descontarStock"/>
    /// en true descuenta stock y registra el movimiento (flujo de pago). En false
    /// solo confirma el estado, sin tocar stock (modo credenciales: el pedido lo
    /// procesa/despacha el sistema comercial, no el ecommerce).
    /// </summary>
    private async Task ConfirmarInternoAsync(int pedidoId, bool descontarStock)
    {
        // La venta automática solo corre en el flujo de PAGO (en modo credenciales la registra Comercial Web)
        // y solo con ecommerce/registrarVentaAlConfirmar = '1'.
        var registrarVenta = descontarStock && await _ventas.HabilitadoAsync();

        // Una sola transacción para estado + venta + stock + movimientos. Si el llamador ya abrió una en este
        // mismo DbContext (PagoEcommerceService.ConfirmarCobroAsync), se participa de esa.
        var txPropia = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync()
            : null;
        try
        {
            // Transición ATÓMICA pendiente_pago → confirmado: si webhook, página y admin confirman a la vez,
            // solo uno afecta la fila y solo ese descuenta stock y registra la venta.
            var filas = await _db.Pedidos
                .Where(p => p.Id == pedidoId && p.EstadoEcommerce == "pendiente_pago")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.EstadoEcommerce, "confirmado")
                    .SetProperty(p => p.Vendido, false)
                    .SetProperty(p => p.Impreso, false));

            if (filas == 0)
            {
                var estado = await _db.Pedidos.Where(p => p.Id == pedidoId)
                    .Select(p => p.EstadoEcommerce).FirstOrDefaultAsync();
                if (estado is null)
                    throw new InvalidOperationException("Pedido no encontrado");
                if (estado == "confirmado")
                {
                    if (txPropia is not null) await txPropia.RollbackAsync();
                    return; // idempotente (reintento de webhook / doble confirmación)
                }
                throw new InvalidOperationException($"El pedido no está en estado pendiente_pago (estado: {estado})");
            }

            if (descontarStock)
            {
                var pedido = await _db.Pedidos
                    .Include(p => p.Detalles).ThenInclude(d => d.Producto).ThenInclude(p => p!.Stock)
                    .FirstAsync(p => p.Id == pedidoId);

                if (registrarVenta)
                {
                    // La venta incluye el descuento de stock y el movimiento tipo 3 (reglas de Comercial Web):
                    // reemplaza al descuento de abajo, nunca se suman.
                    try
                    {
                        await _ventas.RegistrarVentaAsync(pedido);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Confirmación del pedido #{Id} REVERTIDA: falló el registro de la venta. El pedido sigue pendiente de pago; reintentar la confirmación es seguro.",
                            pedidoId);
                        AvisarVentaFallida(pedidoId, ex.Message);
                        throw;
                    }
                }
                else
                {
                    DescontarStockSinVenta(pedido);
                    await _db.SaveChangesAsync();
                }
            }

            if (txPropia is not null) await txPropia.CommitAsync();
        }
        catch
        {
            if (txPropia is not null) await txPropia.RollbackAsync();
            throw;
        }
        finally
        {
            if (txPropia is not null) await txPropia.DisposeAsync();
        }
    }

    /// <summary>Aviso al administrador (en segundo plano) de un pedido pagado que no se pudo confirmar.</summary>
    private void AvisarVentaFallida(int pedidoId, string motivo)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IEmailService>()
                    .EnviarAlertaVentaFallidaAsync(pedidoId, motivo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo avisar por email la falla de venta del pedido #{Id}", pedidoId);
            }
        });
    }

    /// <summary>
    /// Comportamiento previo (sin registro de venta): descuenta stock y registra el movimiento tipo 3.
    /// Se usa cuando ecommerce/registrarVentaAlConfirmar no está activo.
    /// </summary>
    private void DescontarStockSinVenta(Pedido pedido)
    {
        {
            // Descontar stock y registrar movimiento de productos
            foreach (var d in pedido.Detalles)
            {
                if (d.FkProducto is null) continue;

                var stock     = d.Producto?.Stock;
                var stockAnt  = stock?.Cantidad ?? 0m;
                var cantidad  = d.Cantidad      ?? 0m;
                var costoUnit = d.Costo         ?? 0m;

                // Actualizar stock
                if (stock is not null)
                    stock.Cantidad = stockAnt - cantidad;

                // Registrar movimiento (tipo 3 = egreso por venta ecommerce)
                _db.ProductosMovimientos.Add(new Domain.Entities.ProductosMovimiento
                {
                    FkProducto     = d.FkProducto,
                    TipoMovimiento = 3,
                    Descripcion    = d.Producto?.Descripcion ?? d.Descripcion,
                    StockAnt       = stockAnt,
                    StockAct       = stockAnt - cantidad,
                    Costo          = cantidad * costoUnit,
                    Venta          = cantidad * (d.PrecioConIva ?? 0m),
                    Cantidad       = cantidad,
                    FkColor        = d.FkColor,
                    FechaMov       = DateTime.Now
                });
            }
        }
        // El estado (confirmado / vendido / impreso) ya lo dejó el UPDATE atómico de ConfirmarInternoAsync.
    }

    /// <inheritdoc />
    public async Task<int> ConfirmarDirectoAsync(int pedidoId, CheckoutDto datos)
    {
        await PrepararPagoAsync(pedidoId, datos);                         // borrador → pendiente_pago
        await ConfirmarInternoAsync(pedidoId, descontarStock: false);     // pendiente_pago → confirmado (SIN descontar stock)

        // Emails en background — no bloquean la respuesta HTTP. Se crea un scope propio
        // para evitar ObjectDisposedException si el request HTTP finaliza primero.
        //  • Cliente: confirmación del pedido con el comprobante/PDF adjunto.
        //  • Admin: notificación de nuevo pedido.
        var capturedId = pedidoId;
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
            try { await email.EnviarConfirmacionPedidoAsync(capturedId); } catch { }
            try { await email.EnviarNotificacionAdminAsync(capturedId);   } catch { }
        });

        return pedidoId;
    }

    public async Task<PedidoResumenDto?> ObtenerResumenAsync(int pedidoId)
    {
        var pedido = await _db.Pedidos
            .Include(p => p.Detalles)
            .Include(p => p.Cliente)
            .FirstOrDefaultAsync(p => p.Id == pedidoId);

        if (pedido is null) return null;

        // Cobro más reciente del pedido (puede no existir si está en borrador)
        var cobro = await _db.CobrosEcommerce
            .Where(c => c.FkPedido == pedidoId)
            .OrderByDescending(c => c.FechaCreacion)
            .FirstOrDefaultAsync();

        var nombre = pedido.FkCliente.HasValue
            ? (pedido.Cliente?.NombreComercial ?? pedido.NombreCliente ?? string.Empty)
            : (pedido.NombreCliente ?? string.Empty);

        var subtotalProductos = pedido.Detalles.Sum(d => d.Subtotal ?? 0);
        var costoEnvio        = pedido.CostoEnvio ?? 0;

        return new PedidoResumenDto
        {
            Id                  = pedido.Id,
            Fecha               = pedido.Fecha ?? DateTime.Now,
            Estado              = pedido.EstadoEcommerce,
            Total               = pedido.Total ?? 0,
            SubtotalProductos   = subtotalProductos,
            CostoEnvio          = costoEnvio,
            NombreCliente       = nombre,
            EmailCliente        = pedido.FkCliente.HasValue ? pedido.Cliente?.Email : pedido.EmailCliente,
            DireccionEntrega    = pedido.DireccionEntrega,
            Observacion         = pedido.Observacion,
            MetodoPago          = cobro?.Tipo,
            CobroId             = cobro?.Id,
            ClienteId           = pedido.FkCliente,
            GuestToken          = pedido.GuestToken,
            Items               = pedido.Detalles.Select(d => new PedidoDetalleDto
            {
                Descripcion = (d.Descripcion ?? string.Empty) + (d.Descuento == 100m ? " (bonificado)" : ""),
                Cantidad    = d.Cantidad ?? 0,
                Precio      = d.PrecioConIva ?? 0,
                Subtotal    = d.Subtotal ?? 0
            }).ToList()
        };
    }

    public async Task<List<PedidoResumenDto>> ObtenerPorClienteAsync(int clienteId)
    {
        var limite         = DateTime.Now.AddMonths(-3);
        var estadosValidos = new[] { "pendiente_pago", "confirmado" };

        var pedidos = await _db.Pedidos
            .Include(p => p.Detalles)
            .Where(p => p.FkCliente == clienteId
                     && p.EsEcommerce == true
                     && estadosValidos.Contains(p.EstadoEcommerce)
                     && p.Fecha >= limite)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync();

        return pedidos.Select(MapearResumen).ToList();
    }

    public async Task<List<PedidoResumenDto>> ObtenerPorGuestTokenAsync(string guestToken)
    {
        var limite         = DateTime.Now.AddMonths(-3);
        var estadosValidos = new[] { "pendiente_pago", "confirmado" };

        var pedidos = await _db.Pedidos
            .Include(p => p.Detalles)
            .Where(p => p.GuestToken == guestToken
                     && p.EsEcommerce == true
                     && estadosValidos.Contains(p.EstadoEcommerce)
                     && p.Fecha >= limite)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync();

        return pedidos.Select(MapearResumen).ToList();
    }

    private static PedidoResumenDto MapearResumen(Pedido p) => new()
    {
        Id            = p.Id,
        Fecha         = p.Fecha ?? DateTime.Now,
        Estado        = p.EstadoEcommerce,
        Total         = p.Total ?? 0,
        NombreCliente = p.NombreCliente ?? string.Empty,
        Items         = p.Detalles.Select(d => new PedidoDetalleDto
        {
            Descripcion = (d.Descripcion ?? string.Empty) + (d.Descuento == 100m ? " (bonificado)" : ""),
            Cantidad    = d.Cantidad ?? 0,
            Precio      = d.PrecioConIva ?? 0,
            Subtotal    = d.Subtotal ?? 0
        }).ToList()
    };

    public async Task LimpiarBorradoresViejosAsync(int diasAntiguedad = 7)
    {
        var limite = DateTime.Now.AddDays(-diasAntiguedad);
        var viejos = await _db.Pedidos
            .Include(p => p.Detalles)
            .Where(p => p.EsEcommerce == true
                     && p.EstadoEcommerce == "borrador"
                     && p.Fecha < limite)
            .ToListAsync();

        foreach (var p in viejos)
            _db.PedidoDetalles.RemoveRange(p.Detalles);

        _db.Pedidos.RemoveRange(viejos);
        await _db.SaveChangesAsync();
    }
}
