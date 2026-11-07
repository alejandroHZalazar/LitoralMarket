using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Carrusel de promociones de la portada. Las imágenes viven en promocionesCarrusel (longblob),
/// igual que imagenesProductos: los listados se proyectan sin el blob y la imagen se sirve
/// bajo demanda por URL con caché largo (la URL lleva la versión).
/// </summary>
public class PromocionCarruselService : IPromocionCarruselService
{
    public const int MaxBytes  = 5 * 1024 * 1024;
    public const int MaxTitulo = 150;
    private const string CacheKey = "promociones_carrusel";
    private static readonly TimeSpan CacheDuracion = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PromocionCarruselService> _log;

    public PromocionCarruselService(AppDbContext db, IMemoryCache cache, ILogger<PromocionCarruselService> log)
    {
        _db = db;
        _cache = cache;
        _log = log;
    }

    public async Task<List<PromocionCarruselDto>> ListarAsync()
    {
        if (_cache.TryGetValue(CacheKey, out List<PromocionCarruselDto>? lista) && lista is not null)
            return lista;

        // Proyección: nunca materializa la entidad, así que no trae el blob.
        var filas = await _db.PromocionesCarrusel
            .AsNoTracking()
            .OrderBy(p => p.Orden).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.Titulo, p.Orden, p.FechaModificacion })
            .ToListAsync();

        lista = filas.Select(f => new PromocionCarruselDto
        {
            Id      = f.Id,
            Titulo  = f.Titulo,
            Orden   = f.Orden,
            Version = f.FechaModificacion.Ticks
        }).ToList();

        _cache.Set(CacheKey, lista, CacheDuracion);
        return lista;
    }

    public async Task<PromocionImagen?> ObtenerImagenAsync(int id)
    {
        var img = await _db.PromocionesCarrusel
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.Imagen, p.ContentType })
            .FirstOrDefaultAsync();

        if (img is null || img.Imagen.Length == 0) return null;
        return new PromocionImagen(img.Imagen, img.ContentType ?? DetectarTipo(img.Imagen) ?? "image/jpeg");
    }

    public async Task<ResultadoPromocion> CrearAsync(byte[] imagen, string? titulo)
    {
        var error = ValidarImagen(imagen, out var tipo);
        if (error is not null) return ResultadoPromocion.Fallo(error);

        try
        {
            var ultimo = await _db.PromocionesCarrusel.MaxAsync(p => (int?)p.Orden) ?? 0;
            var ahora = DateTime.Now;
            var promo = new PromocionCarrusel
            {
                Imagen = imagen,
                ContentType = tipo,
                Titulo = LimpiarTitulo(titulo),
                Orden = ultimo + 1,
                FechaAlta = ahora,
                FechaModificacion = ahora
            };
            _db.PromocionesCarrusel.Add(promo);
            await _db.SaveChangesAsync();
            Invalidar();
            return ResultadoPromocion.Correcto(promo.Id);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error al crear promoción del carrusel");
            return ResultadoPromocion.Fallo("No se pudo guardar la promoción.");
        }
    }

    public async Task<ResultadoPromocion> ReemplazarImagenAsync(int id, byte[] imagen)
    {
        var error = ValidarImagen(imagen, out var tipo);
        if (error is not null) return ResultadoPromocion.Fallo(error);

        try
        {
            var filas = await _db.PromocionesCarrusel
                .Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Imagen, imagen)
                    .SetProperty(p => p.ContentType, tipo)
                    .SetProperty(p => p.FechaModificacion, DateTime.Now));
            if (filas == 0) return ResultadoPromocion.Fallo("La promoción no existe.");
            Invalidar();
            return ResultadoPromocion.Correcto(id);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error al reemplazar la imagen de la promoción {Id}", id);
            return ResultadoPromocion.Fallo("No se pudo reemplazar la imagen.");
        }
    }

    public async Task<ResultadoPromocion> ActualizarTituloAsync(int id, string? titulo)
    {
        var limpio = LimpiarTitulo(titulo);
        var filas = await _db.PromocionesCarrusel
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Titulo, limpio));
        if (filas == 0) return ResultadoPromocion.Fallo("La promoción no existe.");
        Invalidar();
        return ResultadoPromocion.Correcto(id);
    }

    public async Task<ResultadoPromocion> EliminarAsync(int id)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var filas = await _db.PromocionesCarrusel.Where(p => p.Id == id).ExecuteDeleteAsync();
            if (filas == 0)
            {
                await tx.RollbackAsync();
                return ResultadoPromocion.Fallo("La promoción no existe.");
            }

            await RenumerarAsync();
            await tx.CommitAsync();
            Invalidar();
            return ResultadoPromocion.Correcto();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _log.LogError(ex, "Error al eliminar la promoción {Id}", id);
            return ResultadoPromocion.Fallo("No se pudo eliminar la promoción.");
        }
    }

    public async Task<ResultadoPromocion> ReordenarAsync(IReadOnlyList<int> idsEnOrden)
    {
        if (idsEnOrden is null || idsEnOrden.Count == 0 || idsEnOrden.Distinct().Count() != idsEnOrden.Count)
            return ResultadoPromocion.Fallo("El orden recibido no es válido.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var existentes = await _db.PromocionesCarrusel.Select(p => p.Id).ToListAsync();
            if (existentes.Count != idsEnOrden.Count || existentes.Except(idsEnOrden).Any())
            {
                await tx.RollbackAsync();
                return ResultadoPromocion.Fallo("La lista cambió mientras la editabas. Recargá la página.");
            }

            for (var i = 0; i < idsEnOrden.Count; i++)
            {
                var id = idsEnOrden[i];
                var orden = i + 1;
                await _db.PromocionesCarrusel.Where(p => p.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Orden, orden));
            }

            await tx.CommitAsync();
            Invalidar();
            return ResultadoPromocion.Correcto();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _log.LogError(ex, "Error al reordenar el carrusel");
            return ResultadoPromocion.Fallo("No se pudo guardar el orden.");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Deja el orden consecutivo 1..N respetando el orden actual (sin blobs).</summary>
    private async Task RenumerarAsync()
    {
        var ids = await _db.PromocionesCarrusel
            .OrderBy(p => p.Orden).ThenBy(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync();
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            var orden = i + 1;
            await _db.PromocionesCarrusel.Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Orden, orden));
        }
    }

    private void Invalidar() => _cache.Remove(CacheKey);

    private static string? LimpiarTitulo(string? titulo)
    {
        var t = titulo?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length > MaxTitulo ? t[..MaxTitulo] : t;
    }

    /// <summary>Valida tamaño y tipo REAL (magic bytes), no el que declara el navegador.</summary>
    private static string? ValidarImagen(byte[]? bytes, out string? tipo)
    {
        tipo = null;
        if (bytes is null || bytes.Length == 0) return "Elegí una imagen.";
        if (bytes.Length > MaxBytes) return "La imagen supera los 5 MB.";
        tipo = DetectarTipo(bytes);
        return tipo is null ? "El archivo no es una imagen válida (JPG, PNG, WEBP o GIF)." : null;
    }

    private static string? DetectarTipo(byte[] b) =>
          b.Length >= 3  && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg"
        : b.Length >= 8  && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? "image/png"
        : b.Length >= 6  && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38 ? "image/gif"
        : b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
                         && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50 ? "image/webp"
        : null;
}
