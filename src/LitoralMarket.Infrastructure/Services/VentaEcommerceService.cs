using System.Globalization;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Réplica, para pedidos del ecommerce, de la registración de venta de Comercial Web
/// (Comercial Web: Infrastructure/Services/VentaService.cs, GrabarVentaAsync). Mismas tablas, mismas
/// columnas y mismas fórmulas. Diferencias deliberadas, acordadas con el usuario:
///   • Total = solo productos (sin envío), como cuando Comercial Web carga un pedido.
///   • Invitados (pedido sin cliente) → parámetro existente ventas/clienteConsumidorFinal.
///   • Sin cuenta corriente, cobros, caja, formas de pago ni factura electrónica: el pedido ya está pagado.
///   • Cada línea del pedido marca su propia fila de pedidoDetalle (Comercial Web marca la primera línea
///     con ese producto) y el stock de un producto repetido se descuenta acumulado sobre la misma fila.
/// </summary>
public class VentaEcommerceService : IVentaEcommerceService
{
    private const int TipoMovimientoVenta = 3;   // mismo valor fijo que Comercial Web

    private readonly AppDbContext                    _db;
    private readonly IParametrosService              _params;
    private readonly ILogger<VentaEcommerceService>  _logger;
    private readonly IPromocionesService             _promos;

    public VentaEcommerceService(AppDbContext db, IParametrosService parametros, ILogger<VentaEcommerceService> logger,
                                 IPromocionesService promos)
    {
        _db     = db;
        _params = parametros;
        _logger = logger;
        _promos = promos;
    }

    public async Task<bool> HabilitadoAsync() =>
        await _params.GetValorAsync("ecommerce", "registrarVentaAlConfirmar") == "1";

    public async Task<long?> RegistrarVentaAsync(Pedido pedido)
    {
        // ── 0. Idempotencia: un pedido genera una sola venta ────────────────────
        if (await _db.PedidosVentas.AnyAsync(pv => pv.FkPedido == pedido.Id))
        {
            _logger.LogWarning(
                "VentaEcommerce: el pedido #{Id} ya tiene una venta vinculada (pedidos_ventas) — no se registra otra ni se descuenta stock",
                pedido.Id);
            return null;
        }

        // ── 1. Validaciones ─────────────────────────────────────────────────────
        var lineas = pedido.Detalles
            .Where(d => d.FkProducto is > 0 && (d.Cantidad ?? 0m) > 0m)
            .ToList();
        if (lineas.Count == 0)
            throw new InvalidOperationException($"El pedido #{pedido.Id} no tiene líneas con producto para registrar la venta.");

        var clienteId = await ResolverClienteAsync(pedido);
        var cajeroId  = await LeerEnteroAsync("ecommerce", "cajeroVentaEcommerce");

        var productoIds = lineas.Select(d => d.FkProducto!.Value).Distinct().ToList();

        // Datos del producto leídos frescos de productos (como Comercial Web), no del pedido.
        var productos = await _db.Productos.AsNoTracking()
            .Where(p => productoIds.Contains(p.Id))
            .Select(p => new { p.Id, p.CodBarras, p.CodProveedor, p.Descripcion })
            .ToDictionaryAsync(p => p.Id);

        // Costo ACTUAL (costosProductos), primera fila por producto, como Comercial Web.
        var costos = (await _db.CostosProductos.AsNoTracking()
                .Where(c => c.FkProducto != null && productoIds.Contains(c.FkProducto.Value))
                .OrderBy(c => c.Id)
                .Select(c => new { FkProducto = c.FkProducto!.Value, Costo = c.Costo ?? 0m })
                .ToListAsync())
            .GroupBy(c => c.FkProducto)
            .ToDictionary(g => g.Key, g => g.First().Costo);

        var iva   = pedido.Iva ?? 0m;
        var ahora = DateTime.Now;

        // ── 2. Cálculo de líneas (fórmulas de Comercial Web) ────────────────────
        //   desc/rec % = −descuento + recargo (negativo = descuento)
        //   subtotalSinIva (UNITARIO) = precioSinIva × (1 + descRec/100)
        //   precioConIva = el que pagó el cliente (en el ecommerce ya es el final)
        //   subtotal = cantidad × precioConIva
        //   costo = cantidad × costo actual
        var calculadas = lineas.Select(d =>
        {
            var pid          = d.FkProducto!.Value;
            var cantidad     = d.Cantidad ?? 0m;
            var precioConIva = d.PrecioConIva ?? d.PrecioSinIva ?? 0m;
            var precioSinIva = d.PrecioSinIva ?? precioConIva;
            var descRec      = -(d.Descuento ?? 0m) + (d.Recargo ?? 0m);
            var costoUnit    = costos.TryGetValue(pid, out var c) ? c : 0m;
            return new
            {
                Linea         = d,
                ProductoId    = pid,
                Cantidad      = cantidad,
                PrecioSinIva  = precioSinIva,
                PrecioConIva  = precioConIva,
                SubtotalSinIva = precioSinIva * (1m + descRec / 100m),
                Subtotal      = cantidad * precioConIva,
                Descuento     = descRec < 0m ? -descRec : (decimal?)null,
                Recargo       = descRec > 0m ?  descRec : (decimal?)null,
                CostoUnit     = costoUnit
            };
        }).ToList();

        // ── 3. Cabecera ─────────────────────────────────────────────────────────
        var venta = new Venta
        {
            Fecha      = ahora,
            TotalVenta = calculadas.Sum(x => x.Subtotal),            // solo productos (sin envío)
            TotalCosto = calculadas.Sum(x => x.Cantidad * x.CostoUnit),
            FkCliente  = clienteId,
            FkCajero   = cajeroId,
            Iva        = iva,                                         // porcentaje
            Descuento  = pedido.Descuento,                            // porcentaje
            Recargo    = pedido.Recargo,                              // porcentaje
            FkVendedor = null,
            Comision   = 0m,
            Impuesto   = 0m
        };
        _db.Ventas.Add(venta);
        await _db.SaveChangesAsync();

        // ── 4. Detalle + stock + movimiento tipo 3 + marca en pedidoDetalle ─────
        var stockPorProducto = new Dictionary<int, StockProducto>();
        var detallesVenta    = new List<(PedidoDetalle Pedido, VentaDetalle Venta)>();
        foreach (var x in calculadas)
        {
            productos.TryGetValue(x.ProductoId, out var prod);

            var ventaDetalle = new VentaDetalle
            {
                FkVenta        = venta.Id,
                FkProducto     = x.ProductoId,
                CodBarras      = prod?.CodBarras,
                CodProveedor   = prod?.CodProveedor,
                Descripcion    = prod?.Descripcion ?? x.Linea.Descripcion,
                PrecioSinIva   = x.PrecioSinIva,
                PrecioConIva   = x.PrecioConIva,
                Cantidad       = x.Cantidad,
                Costo          = x.Cantidad * x.CostoUnit,
                Subtotal       = x.Subtotal,
                Descuento      = x.Descuento,
                Recargo        = x.Recargo,
                SubtotalSinIva = x.SubtotalSinIva
            };
            _db.VentasDetalle.Add(ventaDetalle);
            detallesVenta.Add((x.Linea, ventaDetalle));

            // Stock: misma regla que Comercial Web (sin validar; puede quedar negativo; si no hay fila se crea).
            // Un producto repetido en el pedido acumula sobre la misma fila.
            if (!stockPorProducto.TryGetValue(x.ProductoId, out var stock))
            {
                stock = await _db.StockProductos.FirstOrDefaultAsync(s => s.FkProducto == x.ProductoId)
                        ?? _db.StockProductos.Add(new StockProducto { FkProducto = x.ProductoId, Cantidad = 0m }).Entity;
                stockPorProducto[x.ProductoId] = stock;
            }
            var stockAnt = stock.Cantidad ?? 0m;
            stock.Cantidad = stockAnt - x.Cantidad;

            _db.ProductosMovimientos.Add(new ProductosMovimiento
            {
                FkProducto     = x.ProductoId,
                TipoMovimiento = TipoMovimientoVenta,
                Descripcion    = prod?.Descripcion ?? x.Linea.Descripcion,
                StockAnt       = stockAnt,
                StockAct       = stockAnt - x.Cantidad,
                Costo          = x.Cantidad * x.CostoUnit,
                Venta          = x.Cantidad * x.PrecioConIva,
                Cantidad       = x.Cantidad,
                FkColor        = 0,
                FechaMov       = ahora
            });

            x.Linea.Procesado     = true;
            x.Linea.CantEntregada = x.Cantidad;
        }
        await _db.SaveChangesAsync();

        // Promociones (promociones/descuentos = 1): la auditoria del pedido pasa a las lineas de la venta, como hace
        // Comercial Web. Apagado: no hace nada. Las lineas ya tienen id.
        await _promos.CopiarAuditoriaAVentaAsync(detallesVenta.Select(d => (d.Pedido.Linea, d.Venta.Linea)).ToList());

        // ── 5. Pedido vendido + vínculo pedido ↔ venta ──────────────────────────
        await _db.Pedidos.Where(p => p.Id == pedido.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Vendido, true));

        _db.PedidosVentas.Add(new PedidoVenta { FkPedido = pedido.Id, FkVenta = venta.Id, Fecha = ahora });
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "VentaEcommerce: pedido #{PedidoId} → venta #{VentaId} (cliente {Cliente}, total {Total}, {Lineas} línea(s))",
            pedido.Id, venta.Id, clienteId, venta.TotalVenta, calculadas.Count);

        return venta.Id;
    }

    public async Task<DiagnosticoVentaEcommerce> DiagnosticoAsync()
    {
        var habilitado = await HabilitadoAsync();

        bool tablas;
        try
        {
            await _db.Ventas.AsNoTracking().Select(v => v.Id).FirstOrDefaultAsync();
            await _db.VentasDetalle.AsNoTracking().Select(v => v.Linea).FirstOrDefaultAsync();
            await _db.PedidosVentas.AsNoTracking().Select(v => v.Id).FirstOrDefaultAsync();
            tablas = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VentaEcommerce: faltan tablas de ventas (ventas / ventasDetalle / pedidos_ventas)");
            tablas = false;
        }

        var cliente = await LeerEnteroAsync("ventas", "clienteConsumidorFinal");
        var clienteValido = cliente is not null && await _db.Clientes.AnyAsync(c => c.Id == cliente.Value);

        return new DiagnosticoVentaEcommerce(habilitado, tablas, cliente, clienteValido);
    }

    // ─────────────────────────────────────────────────────────────
    private async Task<int> ResolverClienteAsync(Pedido pedido)
    {
        if (pedido.FkCliente is > 0)
            return pedido.FkCliente.Value;

        // Invitado: cliente consumidor final configurado en el sistema comercial.
        var id = await LeerEnteroAsync("ventas", "clienteConsumidorFinal");
        if (id is null)
            throw new InvalidOperationException(
                $"El pedido #{pedido.Id} es de un invitado y no está configurado el parámetro ventas/clienteConsumidorFinal.");

        if (!await _db.Clientes.AnyAsync(c => c.Id == id.Value))
            throw new InvalidOperationException(
                $"El cliente consumidor final #{id} (ventas/clienteConsumidorFinal) no existe.");

        return id.Value;
    }

    private async Task<int?> LeerEnteroAsync(string modulo, string parametro)
    {
        var val = await _params.GetValorAsync(modulo, parametro);
        return int.TryParse(val?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : null;
    }
}
