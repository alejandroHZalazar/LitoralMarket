using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Repositories;

public class DireccionEntregaRepository : IDireccionEntregaRepository
{
    private readonly AppDbContext _db;
    public DireccionEntregaRepository(AppDbContext db) => _db = db;

    public async Task<List<DireccionEntregaDto>> ObtenerActivasAsync()
    {
        var lista = await _db.DireccionesEntrega
            .Where(d => d.Activo)
            .OrderBy(d => d.Orden)
            .ToListAsync();

        return lista.Select(d => new DireccionEntregaDto
        {
            Id           = d.Id,
            Descripcion  = d.Descripcion,
            TipoEntrega  = d.TipoEntrega,
            Direccion    = d.Direccion,
            Localidad    = d.Localidad,
            Referencia   = d.Referencia,
            CostoEnvio   = d.CostoEnvio,
            EsGratis     = d.EsGratis,
            PermiteLibre = d.PermiteLibre,
            EsDefault    = d.EsDefault
        }).ToList();
    }

    public async Task<DireccionEntregaDto?> ObtenerPorIdAsync(int id)
    {
        var d = await _db.DireccionesEntrega.FindAsync(id);
        if (d is null || !d.Activo) return null;

        return new DireccionEntregaDto
        {
            Id           = d.Id,
            Descripcion  = d.Descripcion,
            TipoEntrega  = d.TipoEntrega,
            Direccion    = d.Direccion,
            Localidad    = d.Localidad,
            Referencia   = d.Referencia,
            CostoEnvio   = d.CostoEnvio,
            EsGratis     = d.EsGratis,
            PermiteLibre = d.PermiteLibre,
            EsDefault    = d.EsDefault
        };
    }
}
