using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LitoralMarket.Infrastructure.Services;

public class PedidoService : IPedidoService
{
    private readonly AppDbContext       _db;
    private readonly IEmailService      _email;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDbContextFactory<AppDbContext> _ctxFactory;

    public PedidoService(
        AppDbContext                    db,
        IEmailService                   email,
        IServiceScopeFactory            scopeFactory,
        IDbContextFactory<AppDbContext> ctxFactory)
    {
        _db           = db;
        _email        = email;
        _scopeFactory = scopeFactory;
        _ctxFactory   = ctxFactory;
    }

    public async Task<(bool valido, List<string> errores)> ValidarStockAsync(int pedidoId)
    {
        var errores = new List<string>();

        // Contexto propio de la factory: es una lectura pura, así puede ejecutarse
        // en paralelo con otras lecturas independientes (el DbContext scoped no
        // admite consultas concurrentes).
        await using var db = await _ctxFactory.CreateDbContextAsync();

        // Proyección directa: no materializa la entidad Producto ni carga el blob
        // 'imagen'. Solo trae cantidad solicitada y stock disponible por línea.
        var detalles = await db.PedidoDetalles
            .Where(d => d.FkPedido == pedidoId)
            .Select(d => new
            {
                d.Descripcion,
                d.Cantidad,
                Stock = d.Producto != null && d.Producto.Stock != null
                        ? d.Producto.Stock.Cantidad
                        : (decimal?)0
            })
            .ToListAsync();

        foreach (var d in detalles)
        {
            var stock = d.Stock ?? 0;
            if (stock < (d.Cantidad ?? 0))
                errores.Add($"'{d.Descripcion}': stock disponible {stock:N2}, solicitado {d.Cantidad:N2}");
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
        // Solo se necesita cargar detalles/producto/stock cuando se va a descontar.
        IQueryable<Pedido> query = _db.Pedidos;
        if (descontarStock)
            query = query.Include(p => p.Detalles).ThenInclude(d => d.Producto).ThenInclude(p => p!.Stock);

        var pedido = await query.FirstOrDefaultAsync(p => p.Id == pedidoId);

        if (pedido is null)
            throw new InvalidOperationException("Pedido no encontrado");

        if (pedido.EstadoEcommerce == "confirmado")
            return; // idempotente (reintento de webhook)

        if (pedido.EstadoEcommerce != "pendiente_pago")
            throw new InvalidOperationException($"El pedido no está en estado pendiente_pago (estado: {pedido.EstadoEcommerce})");

        if (descontarStock)
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

        pedido.EstadoEcommerce  = "confirmado";
        pedido.Vendido          = false;
        pedido.Impreso          = false;

        await _db.SaveChangesAsync();
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
                Descripcion = d.Descripcion ?? string.Empty,
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
            Descripcion = d.Descripcion ?? string.Empty,
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
