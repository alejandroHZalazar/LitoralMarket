using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Helpers;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LitoralMarket.Infrastructure.Repositories;

public class ProductoRepository : IProductoRepository
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IParametrosService _parametros;
    private readonly IPromocionesService _promos;
    private const string RubrosCacheKey = "rubros_activos";
    private static readonly TimeSpan RubrosCacheDuration = TimeSpan.FromMinutes(5);
    private const string MenuRubrosCacheKeyConStock  = "rubros_menu_con_stock";
    private const string MenuRubrosCacheKeySoloStock = "rubros_menu_solo_stock";
    private static readonly TimeSpan MenuRubrosCacheDuration = TimeSpan.FromMinutes(2);

    public ProductoRepository(AppDbContext db, IMemoryCache cache, IParametrosService parametros, IPromocionesService promos)
    {
        _db = db;
        _cache = cache;
        _parametros = parametros;
        _promos = promos;
    }

    /// <summary>
    /// Promociones por cantidad / porcentaje (promociones/descuentos = 1): completa en cada producto su promocion vigente
    /// con las reglas cacheadas (sin consulta por producto). Apagado no hace nada.
    /// </summary>
    private async Task<List<ProductoDto>> ConPromosAsync(List<ProductoDto> lista)
    {
        await _promos.AplicarACatalogoAsync(lista);
        return lista;
    }

    /// <summary>
    /// ecommerce/HabilitarVariantesProducto = '1' (valor cacheado). Apagado: ninguna consulta toca las tablas
    /// de variantes y el catálogo se comporta exactamente como siempre.
    /// </summary>
    private Task<bool> VariantesAsync() => _parametros.HabilitarVariantesProductoAsync();

    public async Task<List<ProductoDto>> ObtenerPorRubroAsync(int rubroId, bool incluirSinStock, int pagina, int porPagina)
    {
        var ids = await IdsConDescendientesAsync(rubroId);
        var v = await VariantesAsync();
        var query = EntidadesActivas(v).Where(p => p.FkRubro != null && ids.Contains(p.FkRubro.Value));
        if (!incluirSinStock) query = ConStock(query, v);

        return await ConPromosAsync(await Proyectar(query, v, incluirSinStock)
            .OrderBy(p => p.Descripcion)
            .Skip((pagina - 1) * porPagina)
            .Take(porPagina)
            .ToListAsync());
    }

    public async Task<List<ProductoDto>> BuscarAsync(string termino, bool incluirSinStock, int pagina, int porPagina)
    {
        var v = await VariantesAsync();
        var query = CoincideTexto(EntidadesActivas(v), termino, v);
        if (!incluirSinStock) query = ConStock(query, v);

        return await ConPromosAsync(await Proyectar(query, v, incluirSinStock)
            .OrderBy(p => p.Descripcion)
            .Skip((pagina - 1) * porPagina)
            .Take(porPagina)
            .ToListAsync());
    }

    public async Task<List<ProductoDto>> ObtenerUltimosAsync(int cantidad, bool incluirSinStock)
    {
        // Últimos creados = mayor Id (no existe columna de fecha de alta en el esquema).
        // ORDER BY id DESC usa la PK como índice; LIMIT evita traer toda la tabla.
        // Proyectar() no carga el blob de imagen (solo evalúa imagen IS NOT NULL).
        var v = await VariantesAsync();
        var query = EntidadesActivas(v);
        if (!incluirSinStock) query = ConStock(query, v);

        return await ConPromosAsync(await Proyectar(query, v, incluirSinStock)
            .OrderByDescending(p => p.Id)
            .Take(cantidad)
            .ToListAsync());
    }

    public async Task<List<ProductoDto>> ObtenerSeleccionInicioAsync(bool incluirSinStock, int maximo)
    {
        // Mismos filtros de visibilidad que el catálogo (EntidadesActivas = no dado de baja, ConStock).
        var v = await VariantesAsync();
        var visibles = EntidadesActivas(v);
        if (!incluirSinStock) visibles = ConStock(visibles, v);

        // 1) Ids seleccionados que siguen siendo visibles, en el orden del administrador (consulta liviana).
        var ids = await (from s in _db.ProductosInicio
                         join p in visibles on s.FkProducto equals p.Id
                         orderby s.Orden
                         select p.Id)
                  .Take(maximo)
                  .ToListAsync();
        if (ids.Count == 0) return new List<ProductoDto>();

        // 2) Datos de esos productos (sin blobs) y se reordena en memoria según la selección.
        var dtos = (await Proyectar(visibles.Where(p => ids.Contains(p.Id)), v, incluirSinStock).ToListAsync())
                   .ToDictionary(d => d.Id);
        return await ConPromosAsync(ids.Where(dtos.ContainsKey).Select(i => dtos[i]).ToList());
    }

    public async Task<ProductoDto?> ObtenerPorIdAsync(int id)
    {
        var v = await VariantesAsync();
        // incluirSinStock = true: la ficha se muestra aunque ninguna variante tenga stock (la página decide).
        var dto = await Proyectar(EntidadesActivas(v), v, incluirSinStock: true).FirstOrDefaultAsync(p => p.Id == id);
        if (dto is not null) await _promos.AplicarACatalogoAsync(new List<ProductoDto> { dto });
        return dto;
    }

    public async Task<(List<ProductoDto> Items, int Total)> BuscarPorIdsAsync(
        IReadOnlyCollection<int> ids, bool incluirSinStock, int pagina, int porPagina)
    {
        if (ids.Count == 0) return (new List<ProductoDto>(), 0);
        var v = await VariantesAsync();

        // Con variantes habilitadas, las variantes se muestran como su ficha (que lleva las medidas, colores y la etiqueta
        // de promocion); un producto simple se muestra tal cual. Apagado: se listan los productos como siempre.
        var efectivos = ids.ToList();
        if (v)
        {
            var idsLista = ids.ToList();
            var fichas = await _db.ProductosVariantes.AsNoTracking()
                .Where(x => idsLista.Contains(x.FkProducto))
                .Select(x => new { x.FkProducto, x.FkPrincipal }).ToListAsync();
            var esVariante = fichas.Select(f => f.FkProducto).ToHashSet();
            efectivos = idsLista.Where(i => !esVariante.Contains(i)).Concat(fichas.Select(f => f.FkPrincipal)).Distinct().ToList();
        }

        var query = EntidadesActivas(v).Where(p => efectivos.Contains(p.Id));
        if (!incluirSinStock) query = ConStock(query, v);

        var total = await query.CountAsync();
        var items = await Proyectar(query, v, incluirSinStock)
            .OrderBy(p => p.Descripcion)
            .Skip((Math.Max(1, pagina) - 1) * porPagina)
            .Take(porPagina)
            .ToListAsync();
        return (await ConPromosAsync(items), total);
    }

    public async Task<int?> ObtenerPrincipalDeVarianteAsync(int productoId)
    {
        if (!await VariantesAsync()) return null;
        return await _db.ProductosVariantes
            .Where(x => x.FkProducto == productoId && x.Activa)
            .Select(x => (int?)x.FkPrincipal)
            .FirstOrDefaultAsync();
    }

    public async Task<FichaCatalogoDto?> ObtenerVariantesAsync(int principalId)
    {
        if (!await VariantesAsync()) return null;
        var principal = await _db.ProductosPrincipales
            .Where(f => f.FkProducto == principalId)
            .Select(f => new { f.UsaMedidas, f.UsaColores })
            .FirstOrDefaultAsync();
        if (principal is null) return null;

        // Una consulta: variantes activas y no dadas de baja, con sus datos de venta (sin blobs).
        var filas = await _db.ProductosVariantes
            .Where(x => x.FkPrincipal == principalId && x.Activa && x.Producto!.Baja != true)
            .Select(x => new
            {
                x.FkProducto, x.FkMedida, x.FkColor, x.Orden,
                Fraccionado = x.Producto!.Fraccionado == true,
                Precio = x.Producto!.Precio != null ? (x.Producto.Precio.Precio ?? 0) : 0,
                Stock  = x.Producto.Stock  != null ? (x.Producto.Stock.Cantidad  ?? 0) : 0,
                x.Producto.CodBarras, x.Producto.CodProveedor
            })
            .ToListAsync();

        var medidas = await _db.VariantesMedidas.ToDictionaryAsync(m => m.Id, m => new { m.Nombre, m.Orden });
        var colores = await _db.VariantesColores.ToDictionaryAsync(c => c.Id, c => new { c.Nombre, c.CodigoHex, c.Orden });

        var ficha = new FichaCatalogoDto { PrincipalId = principalId, UsaMedidas = principal.UsaMedidas, UsaColores = principal.UsaColores };
        foreach (var f in filas
                     .OrderBy(f => medidas.TryGetValue(f.FkMedida, out var m) ? m.Orden : int.MaxValue)
                     .ThenBy(f => colores.TryGetValue(f.FkColor, out var c) ? c.Orden : int.MaxValue)
                     .ThenBy(f => f.Orden))
        {
            ficha.Variantes.Add(new VarianteCatalogoDto
            {
                Id = f.FkProducto, MedidaId = f.FkMedida, ColorId = f.FkColor,
                Medida = medidas.TryGetValue(f.FkMedida, out var mm) ? mm.Nombre : null,
                Color = colores.TryGetValue(f.FkColor, out var cc) ? cc.Nombre : null,
                ColorHex = colores.TryGetValue(f.FkColor, out var ch) ? ch.CodigoHex : null,
                Precio = f.Precio, Stock = f.Stock, CodBarras = f.CodBarras, CodProveedor = f.CodProveedor
            });
        }

        // Promociones por cantidad / porcentaje de cada variante (cada variante es un producto con su propia regla).
        // Apagado no hace nada.
        var simulados = filas.Select(f => new ProductoDto { Id = f.FkProducto, Precio = f.Precio, Stock = f.Stock, Fraccionado = f.Fraccionado }).ToList();
        await _promos.AplicarACatalogoAsync(simulados);
        foreach (var v in ficha.Variantes)
        {
            var s = simulados.First(x => x.Id == v.Id);
            v.PrecioPromo = s.PrecioPromo; v.PromoPorcentaje = s.PromoPorcentaje; v.PromoLleva = s.PromoLleva; v.PromoPaga = s.PromoPaga; v.PromoAgrupa = s.PromoAgrupa;
        }
        return ficha;
    }

    public async Task<List<ProductoImagenDto>> ObtenerImagenesAsync(int productoId) =>
        // Solo metadatos (id/principal/orden) — nunca el blob. Principal primero.
        await _db.ImagenesProductos
            .Where(i => i.FkProducto == productoId && !i.Baja)
            .OrderByDescending(i => i.EsPrincipal).ThenBy(i => i.Orden).ThenBy(i => i.Id)
            .Select(i => new ProductoImagenDto { Id = i.Id, EsPrincipal = i.EsPrincipal, Orden = i.Orden })
            .ToListAsync();

    public async Task<List<RubroNodoDto>> ObtenerArbolRubrosAsync()
    {
        // Los rubros cambian rara vez y se consultan en cada request (página + layout).
        // Se cachean unos minutos para evitar la consulta redundante. AsNoTracking:
        // solo se leen para mostrar, no se modifican por este contexto.
        if (_cache.TryGetValue(RubrosCacheKey, out List<RubroNodoDto>? arbol) && arbol is not null)
            return arbol;

        var filas = await _db.Rubros
            .AsNoTracking()
            .Where(r => r.Descripcion != null)
            .Select(r => new { r.Id, Descripcion = r.Descripcion!, r.FkRubroPadre })
            .ToListAsync();

        arbol = RubroArbolHelper.Construir(filas.Select(f => (f.Id, f.Descripcion, f.FkRubroPadre)));

        _cache.Set(RubrosCacheKey, arbol, RubrosCacheDuration);
        return arbol;
    }

    public async Task<List<RubroNodoDto>> ObtenerMenuRubrosAsync(bool incluirSinStock)
    {
        var clave = incluirSinStock ? MenuRubrosCacheKeyConStock : MenuRubrosCacheKeySoloStock;
        if (_cache.TryGetValue(clave, out List<RubroNodoDto>? menu) && menu is not null)
            return menu;

        var arbol = await ObtenerArbolRubrosAsync();

        // UNA consulta: ids de rubro con al menos un producto válido. Usa exactamente los
        // mismos filtros que el catálogo (EntidadesActivas = no dado de baja, ConStock).
        var v = await VariantesAsync();
        var query = EntidadesActivas(v).Where(p => p.FkRubro != null);
        if (!incluirSinStock) query = ConStock(query, v);
        var idsConProductos = await query.Select(p => p.FkRubro!.Value).Distinct().ToListAsync();

        menu = RubroArbolHelper.ConProductos(arbol, idsConProductos.ToHashSet());

        // Vida corta: el stock cambia con las ventas y no debe dejar un subrubro "vacío" visible mucho rato.
        _cache.Set(clave, menu, MenuRubrosCacheDuration);
        return menu;
    }

    public void InvalidarCacheRubros()
    {
        _cache.Remove(RubrosCacheKey);
        _cache.Remove(MenuRubrosCacheKeyConStock);
        _cache.Remove(MenuRubrosCacheKeySoloStock);
    }

    /// <summary>
    /// Ids del rubro y de todos sus subrubros: un producto asignado a un subrubro
    /// (ej. el del proveedor) debe aparecer también al filtrar por su rubro padre.
    /// </summary>
    private async Task<List<int>> IdsConDescendientesAsync(int rubroId) =>
        RubroArbolHelper.IdsConDescendientes(await ObtenerArbolRubrosAsync(), rubroId);

    public async Task<int> ContarPorRubroAsync(int rubroId, bool incluirSinStock)
    {
        var ids = await IdsConDescendientesAsync(rubroId);
        var v = await VariantesAsync();
        var query = EntidadesActivas(v).Where(p => p.FkRubro != null && ids.Contains(p.FkRubro.Value));
        if (!incluirSinStock) query = ConStock(query, v);
        return await query.CountAsync();
    }

    public async Task<int> ContarBusquedaAsync(string termino, bool incluirSinStock)
    {
        var v = await VariantesAsync();
        var query = CoincideTexto(EntidadesActivas(v), termino, v);
        if (!incluirSinStock) query = ConStock(query, v);
        return await query.CountAsync();
    }

    // ── Helpers privados ─────────────────────────────────────────────────────

    /// <summary>
    /// Productos no dados de baja, sin ningún Include (los JOINs se generan en Proyectar).
    /// Con variantes habilitadas: las variantes quedan fuera (se compra desde la ficha) y una ficha sin ninguna
    /// variante activa tampoco se muestra. Subconsultas IN no correlacionadas (se evalúan una vez).
    /// </summary>
    private IQueryable<Producto> EntidadesActivas(bool variantes)
    {
        var q = _db.Productos.Where(p => p.Baja != true);
        if (!variantes) return q;

        var idsVariantes = _db.ProductosVariantes.Select(x => x.FkProducto);
        var idsFichas    = _db.ProductosPrincipales.Select(x => x.FkProducto);
        var fichasConVariantesActivas = _db.ProductosVariantes
            .Where(x => x.Activa && x.Producto!.Baja != true)
            .Select(x => x.FkPrincipal);

        return q.Where(p => !idsVariantes.Contains(p.Id)
                            && (!idsFichas.Contains(p.Id) || fichasConVariantesActivas.Contains(p.Id)));
    }

    /// <summary>
    /// Filtro de stock - opera a nivel de entidad antes de la proyeccion.
    /// EF Core genera el LEFT JOIN a stockProductos sin cargar datos en memoria.
    /// Con variantes: una ficha tiene stock si alguna de sus variantes activas lo tiene.
    /// </summary>
    private IQueryable<Producto> ConStock(IQueryable<Producto> q, bool variantes)
    {
        if (!variantes) return q.Where(p => p.Stock != null && p.Stock.Cantidad > 0);

        var fichasConStock = _db.ProductosVariantes
            .Where(x => x.Activa && x.Producto!.Baja != true && x.Producto.Stock != null && x.Producto.Stock.Cantidad > 0)
            .Select(x => x.FkPrincipal);
        return q.Where(p => (p.Stock != null && p.Stock.Cantidad > 0) || fichasConStock.Contains(p.Id));
    }

    /// <summary>
    /// Busqueda por texto en la descripcion. Con variantes, una ficha tambien coincide si alguna de sus variantes
    /// activas lo hace (por ejemplo buscar "negro" encuentra la remera que tiene la variante "... - Negro").
    /// </summary>
    private IQueryable<Producto> CoincideTexto(IQueryable<Producto> q, string termino, bool variantes)
    {
        if (!variantes) return q.Where(p => p.Descripcion != null && p.Descripcion.Contains(termino));

        var fichasPorVariante = _db.ProductosVariantes
            .Where(x => x.Activa && x.Producto!.Descripcion != null && x.Producto.Descripcion.Contains(termino))
            .Select(x => x.FkPrincipal);
        return q.Where(p => (p.Descripcion != null && p.Descripcion.Contains(termino)) || fichasPorVariante.Contains(p.Id));
    }

    /// <summary>
    /// Proyección SQL directa a ProductoDto.
    /// La columna <c>imagen</c> (longblob) NUNCA se carga: solo se evalúa
    /// <c>imagen IS NOT NULL</c> para determinar si el producto tiene imagen.
    /// EF Core genera los LEFT JOINs necesarios (stock, precio, rubro) automáticamente.
    /// </summary>
    private IQueryable<ProductoDto> Proyectar(IQueryable<Producto> q, bool variantes, bool incluirSinStock)
    {
        if (!variantes) return ProyectarSimple(q);

        // Variantes que cuentan para "Desde $X", stock total y cantidad de opciones: activas, no dadas de baja y,
        // si el catalogo oculta lo agotado, solo las que tienen stock (mismo criterio que ConStock).
        var vv = _db.ProductosVariantes.Where(x => x.Activa && x.Producto!.Baja != true
                    && (incluirSinStock || (x.Producto.Stock != null && x.Producto.Stock.Cantidad > 0)));

        return q.Select(p => new ProductoDto
        {
            Id               = p.Id,
            Descripcion      = p.Descripcion ?? string.Empty,
            DescripcionLarga = p.DescripcionLarga,
            TieneImagen      = p.Imagenes.Any(i => !i.Baja) || p.Imagen != null,
            RubroId          = p.FkRubro,
            RubroNombre      = p.Rubro  != null ? p.Rubro.Descripcion  : null,
            TieneVariantes   = _db.ProductosPrincipales.Any(f => f.FkProducto == p.Id),
            // Ficha: el precio es el menor entre sus variantes visibles; el stock, la suma. Producto simple: como siempre.
            Precio           = _db.ProductosPrincipales.Any(f => f.FkProducto == p.Id)
                                   ? (vv.Where(x => x.FkPrincipal == p.Id)
                                        .Min(x => (decimal?)(x.Producto!.Precio != null ? (x.Producto.Precio.Precio ?? 0) : 0)) ?? 0)
                                   : (p.Precio != null ? (p.Precio.Precio ?? 0) : 0),
            Stock            = _db.ProductosPrincipales.Any(f => f.FkProducto == p.Id)
                                   ? (vv.Where(x => x.FkPrincipal == p.Id)
                                        .Sum(x => (decimal?)(x.Producto!.Stock != null ? (x.Producto.Stock.Cantidad ?? 0) : 0)) ?? 0)
                                   : (p.Stock != null ? (p.Stock.Cantidad ?? 0) : 0),
            CantMedidas      = vv.Where(x => x.FkPrincipal == p.Id && x.FkMedida != 0).Select(x => x.FkMedida).Distinct().Count(),
            CantColores      = vv.Where(x => x.FkPrincipal == p.Id && x.FkColor != 0).Select(x => x.FkColor).Distinct().Count(),
            EsPromocion      = p.EsPromocion ?? false,
            Fraccionado      = p.Fraccionado ?? false,
            CantidadMinimaVenta = p.CantidadMinimaVenta ?? 1,
            CodBarras        = p.CodBarras,
            CodProveedor     = p.CodProveedor,
            Iva              = p.Iva
        });
    }

    private static IQueryable<ProductoDto> ProyectarSimple(IQueryable<Producto> q) =>
        q.Select(p => new ProductoDto
        {
            Id               = p.Id,
            Descripcion      = p.Descripcion ?? string.Empty,
            DescripcionLarga = p.DescripcionLarga,
            // EXISTS en la tabla nueva o el blob legacy (transición). Ninguno carga
            // el blob: se traduce a IS NOT NULL / EXISTS en SQL.
            TieneImagen      = p.Imagenes.Any(i => !i.Baja) || p.Imagen != null,
            RubroId          = p.FkRubro,
            RubroNombre      = p.Rubro  != null ? p.Rubro.Descripcion  : null,
            Precio           = p.Precio != null ? (p.Precio.Precio ?? 0)  : 0,
            Stock            = p.Stock  != null ? (p.Stock.Cantidad  ?? 0)  : 0,
            EsPromocion      = p.EsPromocion ?? false,
            Fraccionado      = p.Fraccionado ?? false,
            CantidadMinimaVenta = p.CantidadMinimaVenta ?? 1,
            CodBarras        = p.CodBarras,
            CodProveedor     = p.CodProveedor,
            Iva              = p.Iva
        });
}
