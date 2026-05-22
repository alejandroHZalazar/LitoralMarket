using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Services;

public class DireccionEntregaAdminService : IDireccionEntregaAdminService
{
    private readonly AppDbContext _db;
    public DireccionEntregaAdminService(AppDbContext db) => _db = db;

    public async Task<List<DireccionEntrega>> ListarAsync() =>
        await _db.DireccionesEntrega
            .OrderBy(d => d.Orden)
            .ThenBy(d => d.Descripcion)
            .ToListAsync();

    public async Task<DireccionEntrega?> ObtenerPorIdAsync(int id) =>
        await _db.DireccionesEntrega.FindAsync(id);

    public async Task<int> CrearAsync(DireccionEntrega d)
    {
        // Orden = último + 1
        var maxOrden = await _db.DireccionesEntrega.MaxAsync(x => (int?)x.Orden) ?? 0;
        d.Orden = maxOrden + 1;

        // Solo una puede ser default
        if (d.EsDefault)
            await _db.DireccionesEntrega
                .Where(x => x.EsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.EsDefault, false));

        // Si EsGratis → costo 0
        if (d.EsGratis) d.CostoEnvio = 0;

        _db.DireccionesEntrega.Add(d);
        await _db.SaveChangesAsync();
        return d.Id;
    }

    public async Task ActualizarAsync(DireccionEntrega d)
    {
        var existing = await _db.DireccionesEntrega.FindAsync(d.Id);
        if (existing is null) return;

        if (d.EsDefault && !existing.EsDefault)
            await _db.DireccionesEntrega
                .Where(x => x.EsDefault && x.Id != d.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.EsDefault, false));

        existing.Descripcion  = d.Descripcion;
        existing.TipoEntrega  = d.TipoEntrega;
        existing.Direccion    = d.Direccion;
        existing.Localidad    = d.Localidad;
        existing.Provincia    = d.Provincia;
        existing.CodigoPostal = d.CodigoPostal;
        existing.Referencia   = d.Referencia;
        existing.EsGratis     = d.EsGratis;
        existing.CostoEnvio   = d.EsGratis ? 0 : d.CostoEnvio;
        existing.PermiteLibre = d.PermiteLibre;
        existing.EsDefault    = d.EsDefault;
        existing.Activo       = d.Activo;

        await _db.SaveChangesAsync();
    }

    public async Task ToggleActivoAsync(int id)
    {
        var d = await _db.DireccionesEntrega.FindAsync(id);
        if (d is null) return;
        d.Activo = !d.Activo;
        await _db.SaveChangesAsync();
    }

    public async Task EliminarAsync(int id)
    {
        var d = await _db.DireccionesEntrega.FindAsync(id);
        if (d is null) return;
        _db.DireccionesEntrega.Remove(d);
        await _db.SaveChangesAsync();
    }

    public async Task MoverAsync(int id, bool subir)
    {
        var todos  = await _db.DireccionesEntrega.OrderBy(x => x.Orden).ToListAsync();
        var idx    = todos.FindIndex(x => x.Id == id);
        var swapIdx = subir ? idx - 1 : idx + 1;

        if (idx < 0 || swapIdx < 0 || swapIdx >= todos.Count) return;

        (todos[idx].Orden, todos[swapIdx].Orden) = (todos[swapIdx].Orden, todos[idx].Orden);
        await _db.SaveChangesAsync();
    }
}
