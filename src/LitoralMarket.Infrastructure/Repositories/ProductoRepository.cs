using LitoralMarket.Application.DTOs;
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

    public ProductoRepository(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<List<ProductoDto>> ObtenerPorRubroAsync(int rubroId, bool incluirSinStock, int pagina, int porPagina)
    {
        var query = EntidadesActivas().Where(p => p.FkRubro == rubroId);
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

    public async Task<ProductoDto?> ObtenerPorIdAsync(int id) =>
        await Proyectar(EntidadesActivas()).FirstOrDefaultAsync(p => p.Id == id);

    public async Task<List<Rubro>> ObtenerRubrosAsync()
    {
        // Los rubros cambian rara vez y se consultan en cada request (página + layout).
        // Se cachean unos minutos para evitar la consulta redundante. AsNoTracking:
        // solo se leen para mostrar, no se modifican por este contexto.
        if (_cache.TryGetValue(RubrosCacheKey, out List<Rubro>? rubros) && rubros is not null)
            return rubros;

        rubros = await _db.Rubros
            .AsNoTracking()
            .Where(r => r.Descripcion != null)
            .OrderBy(r => r.Descripcion)
            .ToListAsync();

        _cache.Set(RubrosCacheKey, rubros, RubrosCacheDuration);
        return rubros;
    }

    public async Task<int> ContarPorRubroAsync(int rubroId, bool incluirSinStock)
    {
        var query = EntidadesActivas().Where(p => p.FkRubro == rubroId);
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
            TieneImagen      = p.Imagen != null,   // IS NOT NULL en SQL — no carga el blob
            RubroId          = p.FkRubro,
            RubroNombre      = p.Rubro  != null ? p.Rubro.Descripcion  : null,
            Precio           = p.Precio != null ? (p.Precio.Precio ?? 0)  : 0,
            Stock            = p.Stock  != null ? (p.Stock.Cantidad  ?? 0)  : 0,
            EsPromocion      = p.EsPromocion ?? false,
            Fraccionado      = p.Fraccionado ?? false,
            CodBarras        = p.CodBarras,
            CodProveedor     = p.CodProveedor,
            Iva              = p.Iva
        });
}
