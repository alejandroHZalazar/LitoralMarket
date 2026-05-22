using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Services;

public class RubroAdminService : IRubroAdminService
{
    private readonly AppDbContext _db;
    public RubroAdminService(AppDbContext db) => _db = db;

    public async Task<List<(int Id, string Descripcion, int CantProductos)>> BuscarAsync(string valor)
    {
        var q = _db.Rubros.AsQueryable();

        if (!string.IsNullOrWhiteSpace(valor))
            q = q.Where(r => r.Descripcion != null && r.Descripcion.Contains(valor));

        return await q
            .OrderBy(r => r.Descripcion)
            .Select(r => new
            {
                r.Id,
                Descripcion   = r.Descripcion ?? string.Empty,
                CantProductos = r.Productos.Count(p => p.Baja != true)
            })
            .ToListAsync()
            .ContinueWith(t => t.Result
                .Select(x => (x.Id, x.Descripcion, x.CantProductos))
                .ToList());
    }

    public async Task<(int Id, string Descripcion)?> ObtenerPorIdAsync(int id)
    {
        var r = await _db.Rubros.FindAsync(id);
        if (r is null) return null;
        return (r.Id, r.Descripcion ?? string.Empty);
    }

    public async Task<int> CrearAsync(string descripcion)
    {
        var rubro = new Rubro { Descripcion = descripcion.Trim() };
        _db.Rubros.Add(rubro);
        await _db.SaveChangesAsync();
        return rubro.Id;
    }

    public async Task ActualizarAsync(int id, string descripcion)
    {
        var r = await _db.Rubros.FindAsync(id);
        if (r is null) return;
        r.Descripcion = descripcion.Trim();
        await _db.SaveChangesAsync();
    }

    public async Task<bool> EliminarAsync(int id)
    {
        var tieneProductos = await _db.Productos
            .AnyAsync(p => p.FkRubro == id && p.Baja != true);

        if (tieneProductos) return false;

        var r = await _db.Rubros.FindAsync(id);
        if (r is null) return true;

        _db.Rubros.Remove(r);
        await _db.SaveChangesAsync();
        return true;
    }
}
