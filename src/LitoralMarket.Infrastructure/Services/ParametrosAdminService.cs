using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LitoralMarket.Infrastructure.Services;

public class ParametrosAdminService : IParametrosAdminService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache  _cache;

    public ParametrosAdminService(AppDbContext db, IMemoryCache cache)
    {
        _db   = db;
        _cache = cache;
    }

    public async Task<List<Parametro>> ListarAsync() =>
        await _db.Parametros
            .OrderBy(p => p.Modulo)
            .ThenBy(p => p.ParametroNombre)
            .ToListAsync();

    public async Task<Parametro?> ObtenerPorIdAsync(int id) =>
        await _db.Parametros.FindAsync(id);

    public async Task CrearAsync(Parametro p)
    {
        _db.Parametros.Add(p);
        await _db.SaveChangesAsync();
        InvalidarCache(p.Modulo, p.ParametroNombre);
    }

    public async Task ActualizarAsync(int id, string? modulo, string? nombre, string? valor, byte[]? imagen, bool quitarImagen)
    {
        var existing = await _db.Parametros.FindAsync(id);
        if (existing is null) return;

        var moduloViejo = existing.Modulo;
        var nombreViejo = existing.ParametroNombre;

        existing.Modulo          = modulo;
        existing.ParametroNombre = nombre;
        existing.Valor           = valor;

        if (quitarImagen)
            existing.Imagen = null;
        else if (imagen is { Length: > 0 })
            existing.Imagen = imagen;
        // si no hay imagen nueva y no se pidió quitar → se mantiene la actual

        await _db.SaveChangesAsync();

        // Invalidar cache de la clave vieja y la nueva (si cambió el nombre)
        InvalidarCache(moduloViejo, nombreViejo);
        InvalidarCache(modulo, nombre);
    }

    public async Task EliminarAsync(int id)
    {
        var p = await _db.Parametros.FindAsync(id);
        if (p is null) return;
        _db.Parametros.Remove(p);
        await _db.SaveChangesAsync();
        InvalidarCache(p.Modulo, p.ParametroNombre);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private void InvalidarCache(string? modulo, string? nombre)
    {
        if (modulo is not null && nombre is not null)
            _cache.Remove($"param_{modulo}_{nombre}");

        // clave especial del logo
        if (modulo == "login" && nombre == "imagen")
            _cache.Remove("param_logo_bytes");
    }
}
