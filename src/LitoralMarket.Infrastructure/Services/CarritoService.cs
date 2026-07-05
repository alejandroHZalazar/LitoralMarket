using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Services;

public class CarritoService : ICarritoService
{
    private readonly AppDbContext                  _db;
    private readonly IParametrosService            _params;
    private readonly IDbContextFactory<AppDbContext> _ctxFactory;

    public CarritoService(
        AppDbContext                  db,
        IParametrosService            parametros,
        IDbContextFactory<AppDbContext> ctxFactory)
    {
        _db         = db;
        _params     = parametros;
        _ctxFactory = ctxFactory;
    }

    /// <summary>
    /// Carga precios, costos y (opcionalmente) stock de los productos indicados,
    /// ejecutando cada consulta EN PARALELO sobre su propio DbContext creado por la
    /// factory. Son lecturas puras sobre tablas distintas → sin riesgo de carrera ni
    /// inconsistencia. El DbContext scoped no admite consultas concurrentes, de ahí
    /// la necesidad de contextos independientes.
    /// </summary>
    private async Task<(Dictionary<int, decimal?> precios,
                        Dictionary<int, decimal?> costos,
                        Dictionary<int, decimal?> stocks)>
        CargarDatosProductosAsync(List<int> productoIds, bool incluirStock)
    {
        await using var ctxPrecios = await _ctxFactory.CreateDbContextAsync();
        await using var ctxCostos  = await _ctxFactory.CreateDbContextAsync();

        var tPrecios = ctxPrecios.PreciosProductos
            .Where(p => productoIds.Contains(p.FkProducto!.Value))
            .ToDictionaryAsync(p => p.FkProducto!.Value, p => p.Precio);

        var tCostos = ctxCostos.CostosProductos
            .Where(c => productoIds.Contains(c.FkProducto!.Value))
            .ToDictionaryAsync(c => c.FkProducto!.Value, c => c.Costo);

        if (!incluirStock)
        {
            await Task.WhenAll(tPrecios, tCostos);
            return (tPrecios.Result, tCostos.Result, new Dictionary<int, decimal?>());
        }

        await using var ctxStock = await _ctxFactory.CreateDbContextAsync();
        var tStocks = ctxStock.StockProductos
            .Where(s => productoIds.Contains(s.FkProducto!.Value))
            .ToDictionaryAsync(s => s.FkProducto!.Value, s => s.Cantidad);

        await Task.WhenAll(tPrecios, tCostos, tStocks);
        return (tPrecios.Result, tCostos.Result, tStocks.Result);
    }

    public async Task<int> ObtenerOCrearBorradorAsync(string guestToken, int? clienteId)
    {
        Pedido? pedido = null;

        if (clienteId.HasValue)
        {
            pedido = await _db.Pedidos.FirstOrDefaultAsync(p =>
                p.FkCliente == clienteId &&
                p.EsEcommerce == true &&
                p.EstadoEcommerce == "borrador");
        }

        if (pedido is null && !string.IsNullOrEmpty(guestToken))
        {
            pedido = await _db.Pedidos.FirstOrDefaultAsync(p =>
                p.GuestToken == guestToken &&
                p.EsEcommerce == true &&
                p.EstadoEcommerce == "borrador");
        }

        if (pedido is not null)
            return pedido.Id;

        pedido = new Pedido
        {
            Fecha           = DateTime.Now,
            EsEcommerce     = true,
            EstadoEcommerce = "borrador",
            FkCliente       = clienteId,
            GuestToken      = guestToken,
            Vendido         = false,
            Impreso         = false
        };

        // En modo credenciales: IVA = 0 y vendedor = parámetro configuracion/admin
        if (await _params.GetModoAccesoAsync() == "credenciales")
        {
            pedido.Iva = 0;
            var vendedorStr = await _params.GetValorAsync("configuracion", "admin");
            if (int.TryParse(vendedorStr, out var vendedorId))
                pedido.FkVendedor = vendedorId;
        }

        _db.Pedidos.Add(pedido);
        await _db.SaveChangesAsync();
        return pedido.Id;
    }

    public async Task AgregarItemAsync(int pedidoId, int productoId, decimal cantidad)
    {
        var existente = await _db.PedidoDetalles
            .FirstOrDefaultAsync(d => d.FkPedido == pedidoId && d.FkProducto == productoId);

        if (existente is not null)
        {
            existente.Cantidad = (existente.Cantidad ?? 0) + cantidad;
            existente.Subtotal       = existente.Cantidad * (existente.PrecioConIva ?? 0);
            existente.SubtotalSinIva = existente.Subtotal;
        }
        else
        {
            // P1-C: proyección directa — no carga el blob Imagen del producto
            var productoData = await _db.Productos
                .Where(p => p.Id == productoId)
                .Select(p => new
                {
                    p.Descripcion,
                    p.CodBarras,
                    p.CodProveedor,
                    Precio = p.Precio != null ? p.Precio.Precio : 0m,
                    Costo  = p.Costo  != null ? p.Costo.Costo   : 0m
                })
                .FirstOrDefaultAsync();

            if (productoData is null) return;

            // P1-B: una sola llamada GetModoAccesoAsync por método
            var modo = await _params.GetModoAccesoAsync();

            var detalle = new PedidoDetalle
            {
                FkPedido       = pedidoId,
                FkProducto     = productoId,
                Descripcion    = productoData.Descripcion,
                CodBarras      = productoData.CodBarras,
                CodProveedor   = productoData.CodProveedor,
                PrecioConIva   = productoData.Precio,
                PrecioSinIva   = productoData.Precio,
                PrecioOrig     = productoData.Precio,
                Costo          = productoData.Costo,
                Cantidad       = cantidad,
                Subtotal       = productoData.Precio * cantidad,
                SubtotalSinIva = productoData.Precio * cantidad,
                FkColor        = 0,
                Procesado      = false
            };

            // En modo credenciales: descuento y recargo explícitamente en cero
            if (modo == "credenciales")
            {
                detalle.Descuento = 0;
                detalle.Recargo   = 0;
            }

            _db.PedidoDetalles.Add(detalle);
        }

        await ActualizarTotalPedidoAsync(pedidoId);
        await _db.SaveChangesAsync();
    }

    public async Task QuitarItemAsync(int pedidoId, long lineaId)
    {
        var detalle = await _db.PedidoDetalles
            .FirstOrDefaultAsync(d => d.Linea == lineaId && d.FkPedido == pedidoId);
        if (detalle is null) return;

        _db.PedidoDetalles.Remove(detalle);
        await ActualizarTotalPedidoAsync(pedidoId);
        await _db.SaveChangesAsync();
    }

    public async Task ActualizarCantidadAsync(int pedidoId, long lineaId, decimal cantidad)
    {
        var detalle = await _db.PedidoDetalles
            .FirstOrDefaultAsync(d => d.Linea == lineaId && d.FkPedido == pedidoId);
        if (detalle is null) return;

        if (cantidad <= 0)
        {
            _db.PedidoDetalles.Remove(detalle);
        }
        else
        {
            detalle.Cantidad = cantidad;
            detalle.Subtotal       = cantidad * (detalle.PrecioConIva ?? 0);
            detalle.SubtotalSinIva = detalle.Subtotal;
        }

        await ActualizarTotalPedidoAsync(pedidoId);
        await _db.SaveChangesAsync();
    }

    public async Task ActualizarPreciosAsync(int pedidoId)
    {
        var detalles = await _db.PedidoDetalles
            .Where(d => d.FkPedido == pedidoId)
            .ToListAsync();

        // P1-C: carga precios y costos con una sola consulta por tabla, sin Include del producto
        var productoIds = detalles
            .Where(d => d.FkProducto.HasValue)
            .Select(d => d.FkProducto!.Value)
            .Distinct()
            .ToList();

        // Precios y costos en paralelo (contextos independientes de la factory).
        var (precios, costos, _) = await CargarDatosProductosAsync(productoIds, incluirStock: false);

        foreach (var d in detalles)
        {
            var pk          = d.FkProducto ?? 0;
            var nuevoPrecio = precios.TryGetValue(pk, out var p) ? p : (d.PrecioConIva ?? 0);
            var nuevoCosto  = costos.TryGetValue(pk, out var c)  ? c : (d.Costo ?? 0);

            d.PrecioConIva   = nuevoPrecio;
            d.PrecioSinIva   = nuevoPrecio;
            d.PrecioOrig     = nuevoPrecio;
            d.Costo          = nuevoCosto;
            d.Subtotal       = nuevoPrecio * (d.Cantidad ?? 0);
            d.SubtotalSinIva = d.Subtotal;
        }

        await ActualizarTotalPedidoAsync(pedidoId);
        await _db.SaveChangesAsync();
    }

    public async Task<List<CarritoItemDto>> ObtenerItemsAsync(int pedidoId)
    {
        // Proyección directa: no carga la entidad Producto completa (evita el blob Imagen).
        // p.Imagen != null se traduce a SQL IS NOT NULL sin leer los bytes del blob.
        var detalles = await _db.PedidoDetalles
            .Where(d => d.FkPedido == pedidoId)
            .Select(d => new
            {
                d.Linea,
                d.FkProducto,
                d.Descripcion,
                d.PrecioConIva,
                d.Subtotal,
                d.Cantidad,
                TieneImagen = d.Producto != null && d.Producto.Imagen != null,
                ProductoId  = d.Producto != null ? (int?)d.Producto.Id : null
            })
            .ToListAsync();

        return detalles.Select(d =>
        {
            var precio   = d.PrecioConIva ?? 0;
            var subtotal = d.Subtotal     ?? 0;

            // Si cantidad es NULL (registro legacy del Comercial), la calculamos
            var cantidad = d.Cantidad > 0
                ? d.Cantidad.Value
                : (precio > 0 ? Math.Round(subtotal / precio, 4) : 0);

            return new CarritoItemDto
            {
                LineaId     = d.Linea,
                ProductoId  = d.FkProducto ?? 0,
                Descripcion = d.Descripcion ?? string.Empty,
                Imagen      = d.TieneImagen && d.ProductoId.HasValue
                              ? $"/images/productos/{d.ProductoId.Value}"
                              : null,
                Precio      = precio,
                Cantidad    = cantidad,
                Subtotal    = subtotal > 0 ? subtotal : precio * cantidad
            };
        }).ToList();
    }

    public async Task<int> ContarItemsAsync(int pedidoId) =>
        await _db.PedidoDetalles.CountAsync(d => d.FkPedido == pedidoId);

    public async Task<decimal> ObtenerTotalAsync(int pedidoId)
    {
        // Siempre calcular desde los detalles para evitar desincronía con el campo total del pedido
        var total = await _db.PedidoDetalles
            .Where(d => d.FkPedido == pedidoId)
            .SumAsync(d => d.Subtotal ?? 0);

        // Actualizar el campo en la cabecera del pedido para mantener consistencia
        var pedido = await _db.Pedidos.FindAsync(pedidoId);
        if (pedido is not null && pedido.Total != total)
        {
            pedido.Total = total;
            await _db.SaveChangesAsync();
        }

        return total;
    }

    public async Task<List<string>> SincronizarCarritoAsync(int pedidoId)
    {
        var eliminados = new List<string>();

        var detalles = await _db.PedidoDetalles
            .Where(d => d.FkPedido == pedidoId)
            .ToListAsync();

        // Carrito vacío: nada que sincronizar. El total ya es consistente (0),
        // así se evitan las consultas de precios/costos/stock y el SaveChanges.
        if (detalles.Count == 0)
            return eliminados;

        // P1-C: cargar precios, costos y stock con consultas directas (sin Include del producto/blob)
        var productoIds = detalles
            .Where(d => d.FkProducto.HasValue)
            .Select(d => d.FkProducto!.Value)
            .Distinct()
            .ToList();

        // Precios, costos y stock en paralelo (3 contextos independientes de la factory).
        var (precios, costos, stocks) = await CargarDatosProductosAsync(productoIds, incluirStock: true);

        foreach (var d in detalles)
        {
            var pk          = d.FkProducto ?? 0;
            var stockActual = stocks.TryGetValue(pk, out var st) ? st : 0m;

            // Eliminar si no tiene stock
            if (stockActual <= 0)
            {
                eliminados.Add(d.Descripcion ?? $"Producto #{d.FkProducto}");
                _db.PedidoDetalles.Remove(d);
                continue;
            }

            // Actualizar precio y costo de lista
            var nuevoPrecio = precios.TryGetValue(pk, out var p) ? p : (d.PrecioConIva ?? 0);
            var nuevoCosto  = costos.TryGetValue(pk, out var c)  ? c : (d.Costo ?? 0);

            d.PrecioConIva   = nuevoPrecio;
            d.PrecioSinIva   = nuevoPrecio;
            d.PrecioOrig     = nuevoPrecio;
            d.Costo          = nuevoCosto;
            d.Subtotal       = nuevoPrecio * (d.Cantidad ?? 0);
            d.SubtotalSinIva = d.Subtotal;
        }

        await ActualizarTotalPedidoAsync(pedidoId);
        await _db.SaveChangesAsync();

        return eliminados;
    }

    public async Task VaciarAsync(int pedidoId)
    {
        var detalles = await _db.PedidoDetalles
            .Where(d => d.FkPedido == pedidoId)
            .ToListAsync();
        _db.PedidoDetalles.RemoveRange(detalles);
        await _db.SaveChangesAsync();
    }

    private async Task ActualizarTotalPedidoAsync(int pedidoId)
    {
        var total = await _db.PedidoDetalles
            .Where(d => d.FkPedido == pedidoId)
            .SumAsync(d => d.Subtotal ?? 0);

        var pedido = await _db.Pedidos.FindAsync(pedidoId);
        if (pedido is not null)
            pedido.Total = total;
    }
}
