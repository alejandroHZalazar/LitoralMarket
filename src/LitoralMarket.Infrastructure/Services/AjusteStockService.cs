using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Helpers;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Ajuste de stock (equivalente al frmAjusteStock de escritorio y a sp_Productos_AjusteStock),
/// con transacción y control de concurrencia: el stock solo se pisa si sigue valiendo lo que el
/// usuario vio, así una venta hecha entre la búsqueda y el guardado nunca se pierde.
/// </summary>
public class AjusteStockService : IAjusteStockService
{
    private const int LimiteBusqueda = 1000;
    private const int MaxLineas      = 1000;

    private readonly AppDbContext _db;
    private readonly IProductoRepository _repo;
    private readonly IParametrosService _parametros;
    private readonly ILogger<AjusteStockService> _log;

    public AjusteStockService(AppDbContext db, IProductoRepository repo,
                              IParametrosService parametros, ILogger<AjusteStockService> log)
    {
        _db = db;
        _repo = repo;
        _parametros = parametros;
        _log = log;
    }

    public async Task<int> DecimalesStockAsync() =>
        int.TryParse(await _parametros.GetValorAsync("productos", "decimalesCant"), out var d) && d is >= 0 and <= 4 ? d : 0;

    public async Task<AjusteStockBusquedaResultado> BuscarAsync(AjusteStockFiltro f)
    {
        var q = _db.Productos.AsNoTracking().AsQueryable();

        if (f.ProveedorId.HasValue)
            q = q.Where(p => p.FkProveedor == f.ProveedorId);

        if (f.RubroId.HasValue)
        {
            // El rubro elegido y todos sus subrubros, desde el árbol cacheado (sin consulta extra).
            var ids = RubroArbolHelper.IdsConDescendientes(await _repo.ObtenerArbolRubrosAsync(), f.RubroId.Value);
            q = q.Where(p => p.FkRubro != null && ids.Contains(p.FkRubro.Value));
        }

        var texto = f.Texto?.Trim();
        if (!string.IsNullOrEmpty(texto))
        {
            q = f.Tipo switch
            {
                0 => q.Where(p => p.CodProveedor != null && p.CodProveedor.Contains(texto)),
                1 => q.Where(p => p.CodBarras    != null && p.CodBarras.Contains(texto)),
                _ => q.Where(p => p.Descripcion  != null && p.Descripcion.Contains(texto))
            };
        }

        if (f.OcultarBajas)
            q = q.Where(p => p.Baja != true);

        // Sin cargar el blob de imagen; un producto sin registro de stock se lista con 0.
        var filas = await q
            .OrderBy(p => p.Descripcion)
            .Take(LimiteBusqueda + 1)
            .Select(p => new AjusteStockItemDto
            {
                Id           = p.Id,
                CodProveedor = p.CodProveedor,
                CodBarras    = p.CodBarras,
                Descripcion  = p.Descripcion ?? string.Empty,
                Stock        = p.Stock != null ? (p.Stock.Cantidad ?? 0m) : 0m,
                Baja         = p.Baja == true
            })
            .ToListAsync();

        var truncado = filas.Count > LimiteBusqueda;
        if (truncado) filas.RemoveAt(filas.Count - 1);
        return new AjusteStockBusquedaResultado { Items = filas, Truncado = truncado, Limite = LimiteBusqueda };
    }

    public async Task<AjusteStockResultado> AjustarAsync(IReadOnlyList<AjusteStockLinea> lineas)
    {
        if (lineas is not { Count: > 0 })
            return AjusteStockResultado.Fallo("No hay ajustes para guardar.");
        if (lineas.Count > MaxLineas)
            return AjusteStockResultado.Fallo($"Se pueden guardar hasta {MaxLineas} ajustes por vez.");
        if (lineas.GroupBy(l => l.ProductoId).Any(g => g.Count() > 1))
            return AjusteStockResultado.Fallo("Hay productos repetidos en el envío.");

        var dec = await DecimalesStockAsync();
        foreach (var l in lineas)
        {
            if (l.StockNuevo < 0)
                return AjusteStockResultado.Fallo("El stock no puede ser negativo.");
            if (Math.Round(l.StockNuevo, dec) != l.StockNuevo)
                return AjusteStockResultado.Fallo(dec == 0
                    ? "El stock debe ser un número entero."
                    : $"El stock admite hasta {dec} decimales.");
        }

        // Líneas sin diferencia: no generan movimiento (el formulario original grababa ajustes en 0).
        var cambios = lineas.Where(l => l.StockNuevo != l.StockLeido).ToList();
        if (cambios.Count == 0)
            return new AjusteStockResultado { Ok = true, Ajustados = 0 };

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var tipoAlta = await TipoMovimientoAsync("AJUSTE ALTA");
            var tipoBaja = await TipoMovimientoAsync("AJUSTE BAJA");
            if (tipoAlta is null || tipoBaja is null)
            {
                await tx.RollbackAsync();
                return AjusteStockResultado.Fallo(
                    "Faltan los tipos de movimiento AJUSTE ALTA / AJUSTE BAJA en esta base. No se realizaron cambios.");
            }

            var ids = cambios.Select(c => c.ProductoId).ToList();
            var descripciones = await _db.Productos
                .Where(p => ids.Contains(p.Id))
                .Select(p => new { p.Id, p.Descripcion })
                .ToDictionaryAsync(p => p.Id, p => p.Descripcion);

            var faltantes = ids.Where(i => !descripciones.ContainsKey(i)).ToList();
            if (faltantes.Count > 0)
            {
                await tx.RollbackAsync();
                return AjusteStockResultado.Fallo($"Productos no encontrados: {string.Join(", ", faltantes)}.");
            }

            var conflictos = new List<int>();
            foreach (var c in cambios)
            {
                // UPDATE condicional atómico: solo pisa el stock si sigue valiendo lo que se leyó.
                var filas = await _db.StockProductos
                    .Where(s => s.FkProducto == c.ProductoId && (s.Cantidad ?? 0m) == c.StockLeido)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Cantidad, c.StockNuevo));

                if (filas > 0) continue;

                var existe = await _db.StockProductos.AnyAsync(s => s.FkProducto == c.ProductoId);
                if (!existe && c.StockLeido == 0m)
                    _db.StockProductos.Add(new StockProducto { FkProducto = c.ProductoId, Cantidad = c.StockNuevo });
                else
                    conflictos.Add(c.ProductoId);
            }

            if (conflictos.Count > 0)
            {
                await tx.RollbackAsync();
                var actuales = await _db.StockProductos
                    .AsNoTracking()
                    .Where(s => s.FkProducto != null && conflictos.Contains(s.FkProducto.Value))
                    .ToDictionaryAsync(s => s.FkProducto!.Value, s => s.Cantidad ?? 0m);
                return new AjusteStockResultado
                {
                    Ok = false,
                    Error = "El stock de algunos productos cambió mientras editabas. No se aplicó ningún ajuste.",
                    Conflictos = conflictos
                        .Select(i => new AjusteStockConflicto(i, actuales.GetValueOrDefault(i, 0m)))
                        .ToList()
                };
            }

            var ahora = DateTime.Now;
            foreach (var c in cambios)
            {
                var dif = c.StockNuevo - c.StockLeido;
                _db.ProductosMovimientos.Add(new ProductosMovimiento
                {
                    FkProducto     = c.ProductoId,
                    TipoMovimiento = dif > 0 ? tipoAlta : tipoBaja,
                    Descripcion    = descripciones[c.ProductoId],
                    StockAnt       = c.StockLeido,
                    StockAct       = c.StockNuevo,
                    Cantidad       = Math.Abs(dif),
                    FechaMov       = ahora
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _log.LogError(ex, "Error al ajustar stock ({Cantidad} productos)", cambios.Count);
            return AjusteStockResultado.Fallo("Error inesperado al guardar el ajuste. No se realizaron cambios.");
        }

        // El menú y el catálogo dependen de stock > 0: se refresca el caché de rubros.
        _repo.InvalidarCacheRubros();
        return new AjusteStockResultado { Ok = true, Ajustados = cambios.Count };
    }

    public async Task<bool> CambiarBajaAsync(int productoId, bool baja)
    {
        var filas = await _db.Productos
            .Where(p => p.Id == productoId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Baja, baja));
        if (filas > 0) _repo.InvalidarCacheRubros();
        return filas > 0;
    }

    private async Task<int?> TipoMovimientoAsync(string descripcion)
    {
        var ids = await _db.Database
            .SqlQuery<int>($"SELECT id AS Value FROM tipoProductosMovimientos WHERE descripcion = {descripcion} LIMIT 1")
            .ToListAsync();
        return ids.Count > 0 ? ids[0] : null;
    }
}
