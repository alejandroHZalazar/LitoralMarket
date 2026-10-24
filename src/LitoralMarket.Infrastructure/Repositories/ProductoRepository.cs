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
    private const string RubrosCacheKey = "rubros_activos";
    private static readonly TimeSpan RubrosCacheDuration = TimeSpan.FromMinutes(5);
    private const string MenuRubrosCacheKeyConStock  = "rubros_menu_con_stock";
    private const string MenuRubrosCacheKeySoloStock = "rubros_menu_solo_stock";
    private static readonly TimeSpan MenuRubrosCacheDuration = TimeSpan.FromMinutes(2);

    public ProductoRepository(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<List<ProductoDto>> ObtenerPorRubroAsync(int rubroId, bool incluirSinStock, int pagina, int porPagina)
    {
        var ids = await IdsConDescendientesAsync(rubroId);
        var query = EntidadesActivas().Where(p => p.FkRubro != null && ids.Contains(p.FkRubro.Value));
        if (!incluirSinStock) query = ConStock(query);

        return await Proyectar(query)
            .OrderBy(p => p.Descripcion)
            .Skip((pagina - 1) * porPagina)
            .Take(porPagina)
            .ToListAsync();
    }

    public async Task<List<ProductoDto>> BuscarAsync(string termino, bool incluirSinStock, int pagina, int porPagina)
    {
        var query = EntidadesActivas()
            .Where(p => p.Descripcion != null && p.Descripcion.Contains(termino));
        if (!incluirSinStock) query = ConStock(query);

        return await Proyectar(query)
            .OrderBy(p => p.Descripcion)
            .Skip((pagina - 1) * porPagina)
            .Take(porPagina)
            .ToListAsync();
    }

    public async Task<List<ProductoDto>> ObtenerUltimosAsync(int cantidad, bool incluirSinStock)
    {
        // Últimos creados = mayor Id (no existe columna de fecha de alta en el esquema).
        // ORDER BY id DESC usa la PK como índice; LIMIT evita traer toda la tabla.
        // Proyectar() no carga el blob de imagen (solo evalúa imagen IS NOT NULL).
        var query = EntidadesActivas();
        if (!incluirSinStock) query = ConStock(query);

        return await Proyectar(query)
            .OrderByDescending(p => p.Id)
            .Take(cantidad)
            .ToListAsync();
    }

    public async Task<ProductoDto?> ObtenerPorIdAsync(int id) =>
        await Proyectar(EntidadesActivas()).FirstOrDefaultAsync(p => p.Id == id);

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
        var query = EntidadesActivas().Where(p => p.FkRubro != null);
        if (!incluirSinStock) query = ConStock(query);
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
        var query = EntidadesActivas().Where(p => p.FkRubro != null && ids.Contains(p.FkRubro.Value));
        if (!incluirSinStock) query = ConStock(query);
        return await query.CountAsync();
    }

    public async Task<int> ContarBusquedaAsync(string termino, bool incluirSinStock)
    {
        var query = EntidadesActivas()
            .Where(p => p.Descripcion != null && p.Descripcion.Contains(termino));
        if (!incluirSinStock) query = ConStock(query);
        return await query.CountAsync();
    }

    // ── Helpers privados ─────────────────────────────────────────────────────

    /// <summary>Productos no dados de baja, sin ningún Include (los JOINs se generan en Proyectar).</summary>
    private IQueryable<Producto> EntidadesActivas() =>
        _db.Productos.Where(p => p.Baja != true);

    /// <summary>
    /// Filtro de stock — opera a nivel de entidad antes de la proyección.
    /// EF Core genera el LEFT JOIN a stockProductos sin cargar datos en memoria.
    /// </summary>
    private static IQueryable<Producto> ConStock(IQueryable<Producto> q) =>
        q.Where(p => p.Stock != null && p.Stock.Cantidad > 0);

    /// <summary>
    /// Proyección SQL directa a ProductoDto.
    /// La columna <c>imagen</c> (longblob) NUNCA se carga: solo se evalúa
    /// <c>imagen IS NOT NULL</c> para determinar si el producto tiene imagen.
    /// EF Core genera los LEFT JOINs necesarios (stock, precio, rubro) automáticamente.
    /// </summary>
    private static IQueryable<ProductoDto> Proyectar(IQueryable<Producto> q) =>
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
