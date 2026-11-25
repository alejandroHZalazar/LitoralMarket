using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Helpers;
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
    private readonly IPromocionesService           _promos;

    public CarritoService(
        AppDbContext                  db,
        IParametrosService            parametros,
        IDbContextFactory<AppDbContext> ctxFactory,
        IPromocionesService           promos)
    {
        _db         = db;
        _params     = parametros;
        _ctxFactory = ctxFactory;
        _promos     = promos;
    }

    /// <summary>
    /// Promociones por cantidad / porcentaje (promociones/descuentos = 1): recalcula las lineas del carrito en el
    /// servidor despues de cada cambio. Apagado no hace nada ni consulta nada: el carrito se arma exactamente como siempre.
    /// </summary>
    private async Task AplicarPromocionesAsync(int pedidoId)
    {
        if (!await _promos.HabilitadoAsync()) return;
        await _db.SaveChangesAsync();                    // lo pendiente (linea nueva, cantidad, bajas) antes de recalcular
        await _promos.AplicarAlCarritoAsync(pedidoId);   // recalcula descuentos / precios / subtotales y persiste
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

    /// <summary>
    /// Busca el pedido borrador existente del cliente/invitado, sin crear uno nuevo
    /// si no hay. Se usa para mostrar el contador del carrito (header) en cualquier
    /// página sin generar pedidos vacíos por el solo hecho de navegar el sitio.
    /// </summary>
    public async Task<int?> BuscarBorradorAsync(string guestToken, int? clienteId)
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

        return pedido?.Id;
    }

    public async Task<int> ObtenerOCrearBorradorAsync(string guestToken, int? clienteId)
    {
        var existente = await BuscarBorradorAsync(guestToken, clienteId);
        if (existente.HasValue)
            return existente.Value;

        var pedido = new Pedido
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
        // Cantidad mínima de venta — proyección directa (sin cargar el blob Imagen).
        var cantidadMinimaVenta = await _db.Productos
            .Where(p => p.Id == productoId)
            .Select(p => (decimal?)p.CantidadMinimaVenta)
            .FirstOrDefaultAsync();

        // Cantidad mínima de venta: la cantidad agregada/incrementada debe ser un
        // múltiplo positivo exacto (si el producto no la exige, cualquier positiva vale).
        if (CantidadVentaHelper.RequiereMultiplo(cantidadMinimaVenta) &&
            !CantidadVentaHelper.EsCantidadValida(cantidad, cantidadMinimaVenta))
            throw new InvalidOperationException(
                $"Este producto se vende en múltiplos de {cantidadMinimaVenta}.");

        // Por defecto (ecommerce/LimitarCantidadPorStock distinto de '1'): sin restricción de cantidad
        // vs stock cuando hay stock disponible (stock > 0): se puede pedir cualquier cantidad. La única
        // restricción de stock es que el producto exista con stock > 0, lo cual ya filtra el catálogo
        // (TieneStock) y SincronizarCarritoAsync (retira del carrito lo que se quedó sin stock).
        var existente = await _db.PedidoDetalles
            .FirstOrDefaultAsync(d => d.FkPedido == pedidoId && d.FkProducto == productoId);

        // Con LimitarCantidadPorStock = '1': lo que ya hay en la línea + lo que se agrega no puede superar el stock.
        if (await _params.LimitarCantidadPorStockAsync())
            await ValidarLimiteStockAsync(productoId, existente?.Cantidad ?? 0m, cantidad);

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

        await AplicarPromocionesAsync(pedidoId);
        await ActualizarTotalPedidoAsync(pedidoId);
        await _db.SaveChangesAsync();
    }

    public async Task QuitarItemAsync(int pedidoId, long lineaId)
    {
        var detalle = await _db.PedidoDetalles
            .FirstOrDefaultAsync(d => d.Linea == lineaId && d.FkPedido == pedidoId);
        if (detalle is null) return;

        _db.PedidoDetalles.Remove(detalle);
        await AplicarPromocionesAsync(pedidoId);
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
            // Cantidad mínima de venta: mismo múltiplo exigido al agregar.
            var cantidadMinimaVenta = detalle.FkProducto.HasValue
                ? await _db.Productos.Where(p => p.Id == detalle.FkProducto.Value)
                    .Select(p => p.CantidadMinimaVenta).FirstOrDefaultAsync()
                : null;

            if (!CantidadVentaHelper.EsCantidadValida(cantidad, cantidadMinimaVenta))
                throw new InvalidOperationException(
                    $"Este producto se vende en múltiplos de {cantidadMinimaVenta}.");

            // Con LimitarCantidadPorStock = '1' la nueva cantidad (que reemplaza a la de la línea) no puede superar el stock.
            if (detalle.FkProducto.HasValue && await _params.LimitarCantidadPorStockAsync())
                await ValidarLimiteStockAsync(detalle.FkProducto.Value, 0m, cantidad);

            detalle.Cantidad = cantidad;
            detalle.Subtotal       = cantidad * (detalle.PrecioConIva ?? 0);
            detalle.SubtotalSinIva = detalle.Subtotal;
        }

        await AplicarPromocionesAsync(pedidoId);
        await ActualizarTotalPedidoAsync(pedidoId);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Solo con ecommerce/LimitarCantidadPorStock = '1'. Lanza si <paramref name="yaEnCarrito"/> +
    /// <paramref name="solicitada"/> supera el máximo permitido por el stock. Lectura pura: no modifica stock.
    /// </summary>
    private async Task ValidarLimiteStockAsync(int productoId, decimal yaEnCarrito, decimal solicitada)
    {
        // Proyección directa (sin blob de imagen): descripción, regla de venta y stock en una sola consulta.
        var info = await _db.Productos
            .Where(p => p.Id == productoId)
            .Select(p => new
            {
                p.Descripcion,
                p.CantidadMinimaVenta,
                Fraccionado = p.Fraccionado == true,
                Stock       = p.Stock != null ? (p.Stock.Cantidad ?? 0m) : 0m
            })
            .FirstOrDefaultAsync();

        if (info is null) return;   // producto inexistente: se mantiene el comportamiento actual

        var maximo = CantidadVentaHelper.MaximoPermitido(info.Stock, info.CantidadMinimaVenta, info.Fraccionado);
        if (yaEnCarrito + solicitada > maximo)
            throw new InvalidOperationException(CantidadVentaHelper.MensajeSuperaStock(
                info.Descripcion, info.Stock, maximo, yaEnCarrito,
                CantidadVentaHelper.UnidadMinima(info.CantidadMinimaVenta, info.Fraccionado)));
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

        await AplicarPromocionesAsync(pedidoId);
        await ActualizarTotalPedidoAsync(pedidoId);
        await _db.SaveChangesAsync();
    }

    public async Task<List<CarritoItemDto>> ObtenerItemsAsync(int pedidoId)
    {
        // Proyección directa: no carga la entidad Producto completa (evita el blob Imagen).
        // TieneImagen se traduce a EXISTS / IS NOT NULL en SQL, sin leer los bytes del blob.
        var detalles = await _db.PedidoDetalles
            .Where(d => d.FkPedido == pedidoId)
            .OrderBy(d => d.Linea)      // mismo orden que usa el calculo de promociones (las bonificadas NxM se asignan en orden de linea)
            .Select(d => new
            {
                d.Linea,
                d.FkProducto,
                d.Descripcion,
                d.PrecioConIva,
                d.PrecioOrig,
                d.Subtotal,
                d.Cantidad,
                // Imágenes en la tabla nueva o el blob legacy (transición). Ninguno carga el blob.
                TieneImagen = d.Producto != null &&
                              (d.Producto.Imagenes.Any(i => !i.Baja) || d.Producto.Imagen != null),
                ProductoId  = d.Producto != null ? (int?)d.Producto.Id : null,
                CantidadMinimaVenta = d.Producto != null ? d.Producto.CantidadMinimaVenta : null,
                Fraccionado = d.Producto != null && d.Producto.Fraccionado == true
            })
            .ToListAsync();

        // Con LimitarCantidadPorStock = '1' se calcula el máximo por línea: una consulta de stock, solo en ese caso.
        Dictionary<int, decimal>? stocks = null;
        if (await _params.LimitarCantidadPorStockAsync())
        {
            var ids = detalles.Where(d => d.FkProducto.HasValue).Select(d => d.FkProducto!.Value).Distinct().ToList();
            stocks = await _db.StockProductos
                .Where(s => s.FkProducto != null && ids.Contains(s.FkProducto.Value))
                .ToDictionaryAsync(s => s.FkProducto!.Value, s => s.Cantidad ?? 0m);
        }

        // Variantes de producto (ecommerce/HabilitarVariantesProducto = 1): una variante sin imagen propia muestra la de su
        // ficha. Una consulta, solo con el parametro encendido y solo si hay lineas sin imagen.
        Dictionary<int, int>? imagenDeFicha = null;
        if (await _params.HabilitarVariantesProductoAsync())
        {
            var sinImagen = detalles.Where(d => d.ProductoId.HasValue && !d.TieneImagen)
                                    .Select(d => d.ProductoId!.Value).Distinct().ToList();
            if (sinImagen.Count > 0)
                imagenDeFicha = await _db.ProductosVariantes
                    .Where(x => sinImagen.Contains(x.FkProducto)
                                && (x.Principal!.Producto!.Imagenes.Any(i => !i.Baja) || x.Principal.Producto.Imagen != null))
                    .ToDictionaryAsync(x => x.FkProducto, x => x.FkPrincipal);
        }

        // Promociones vigentes (solo con promociones/descuentos = 1; apagado devuelve vacio sin consultar nada).
        // Lo que se muestra sale del mismo calculo que persistio AplicarAlCarritoAsync.
        var promo = await _promos.EvaluarAsync(detalles
            .Where(x => x.FkProducto.HasValue)
            .Select(x => (x.FkProducto!.Value, x.Cantidad ?? 0m, x.PrecioOrig ?? x.PrecioConIva ?? 0m)));

        return detalles.Select(d =>
        {
            var precio   = d.PrecioConIva ?? 0;
            PromoLineaCarritoDto? pl = null;
            if (d.FkProducto.HasValue) promo.PorProducto.TryGetValue(d.FkProducto.Value, out pl);
            PromoProximaCarritoDto? px = null;
            if (d.FkProducto.HasValue) promo.Proximas.TryGetValue(d.FkProducto.Value, out px);
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
                              : (d.ProductoId.HasValue && imagenDeFicha is not null && imagenDeFicha.TryGetValue(d.ProductoId.Value, out var fichaId)
                                    ? $"/images/productos/{fichaId}"
                                    : null),
                Precio      = precio,
                Cantidad    = cantidad,
                Subtotal    = subtotal > 0 ? subtotal : precio * cantidad,
                CantidadMinimaVenta = d.CantidadMinimaVenta ?? 1,
                Fraccionado = d.Fraccionado,
                PrecioOriginal = pl is { Tipo: "PORC" } ? d.PrecioOrig : null,
                PromoTexto     = pl?.Texto,
                PromoAhorro    = pl?.Ahorro ?? 0m,
                PromoProxima   = px?.Texto,
                MaximoPermitido = stocks is null
                    ? null
                    : CantidadVentaHelper.MaximoPermitido(
                        stocks.GetValueOrDefault(d.FkProducto ?? 0), d.CantidadMinimaVenta, d.Fraccionado)
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

    public async Task<SincronizacionCarrito> SincronizarCarritoAsync(int pedidoId)
    {
        var eliminados = new List<string>();
        var ajustados  = new List<string>();

        var detalles = await _db.PedidoDetalles
            .Where(d => d.FkPedido == pedidoId)
            .ToListAsync();

        // Carrito vacío: nada que sincronizar. El total ya es consistente (0),
        // así se evitan las consultas de precios/costos/stock y el SaveChanges.
        if (detalles.Count == 0)
            return new SincronizacionCarrito(eliminados, ajustados);

        // P1-C: cargar precios, costos y stock con consultas directas (sin Include del producto/blob)
        var productoIds = detalles
            .Where(d => d.FkProducto.HasValue)
            .Select(d => d.FkProducto!.Value)
            .Distinct()
            .ToList();

        // Precios, costos y stock en paralelo (3 contextos independientes de la factory).
        var (precios, costos, stocks) = await CargarDatosProductosAsync(productoIds, incluirStock: true);

        // Con LimitarCantidadPorStock = '1' hacen falta la regla de venta de cada producto (múltiplo / fraccionado):
        // una consulta extra, solo en ese caso. Los stocks ya están cargados arriba.
        var limitar = await _params.LimitarCantidadPorStockAsync();
        var reglas  = new Dictionary<int, (decimal? Minima, bool Fraccionado)>();
        if (limitar)
        {
            reglas = (await _db.Productos
                    .Where(p => productoIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.CantidadMinimaVenta, Fraccionado = p.Fraccionado == true })
                    .ToListAsync())
                .ToDictionary(p => p.Id, p => (p.CantidadMinimaVenta, p.Fraccionado));
        }

        // Variantes de producto (ecommerce/HabilitarVariantesProducto = 1): una combinacion deshabilitada (o una ficha dada
        // de baja) tiene baja = 1 pero puede conservar stock; si quedo en un carrito hay que retirarla. Una consulta, solo
        // con el parametro encendido: apagado, el carrito se sincroniza exactamente como siempre.
        var dadosDeBaja = new HashSet<int>();
        if (await _params.HabilitarVariantesProductoAsync())
        {
            dadosDeBaja = (await _db.Productos
                    .Where(p => productoIds.Contains(p.Id) && p.Baja == true)
                    .Select(p => p.Id)
                    .ToListAsync())
                .ToHashSet();
        }

        foreach (var d in detalles)
        {
            var pk          = d.FkProducto ?? 0;
            var stockActual = stocks.TryGetValue(pk, out var st) ? st : 0m;

            if (dadosDeBaja.Contains(pk))
            {
                eliminados.Add($"{d.Descripcion ?? $"Producto #{d.FkProducto}"} (ya no está disponible)");
                _db.PedidoDetalles.Remove(d);
                continue;
            }

            // Eliminar si no tiene stock
            if (stockActual <= 0)
            {
                eliminados.Add(d.Descripcion ?? $"Producto #{d.FkProducto}");
                _db.PedidoDetalles.Remove(d);
                continue;
            }

            // Límite por stock: si la cantidad supera lo disponible (el stock bajó después de agregarla) se
            // reduce al máximo y se avisa; si ni siquiera alcanza para la venta mínima, se quita la línea.
            if (limitar && reglas.TryGetValue(pk, out var regla))
            {
                var maximo = CantidadVentaHelper.MaximoPermitido(stockActual ?? 0m, regla.Minima, regla.Fraccionado);
                var unidad = CantidadVentaHelper.UnidadMinima(regla.Minima, regla.Fraccionado);
                var nombre = d.Descripcion ?? $"Producto #{d.FkProducto}";

                if (maximo < unidad)
                {
                    eliminados.Add($"{nombre} (el stock disponible no alcanza para la cantidad mínima de venta)");
                    _db.PedidoDetalles.Remove(d);
                    continue;
                }

                var pedida = d.Cantidad ?? 0m;
                if (pedida > maximo)
                {
                    ajustados.Add($"{nombre}: de {CantidadVentaHelper.Formatear(pedida)} a {CantidadVentaHelper.Formatear(maximo)} unidades");
                    d.Cantidad = maximo;   // el subtotal se recalcula abajo con el precio actualizado
                }
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

        await AplicarPromocionesAsync(pedidoId);
        await ActualizarTotalPedidoAsync(pedidoId);
        await _db.SaveChangesAsync();

        return new SincronizacionCarrito(eliminados, ajustados);
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
