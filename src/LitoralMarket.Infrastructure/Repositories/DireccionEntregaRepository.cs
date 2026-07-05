using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Repositories;

public class DireccionEntregaRepository : IDireccionEntregaRepository
{
    // Repositorio de solo lectura: cada método usa un DbContext propio de la factory.
    // Así las consultas son independientes y pueden ejecutarse en paralelo con otras
    // (p.ej. en el checkout, junto con la lectura de items y la validación de stock).
    private readonly IDbContextFactory<AppDbContext> _ctxFactory;
    public DireccionEntregaRepository(IDbContextFactory<AppDbContext> ctxFactory) => _ctxFactory = ctxFactory;

    public async Task<List<DireccionEntregaDto>> ObtenerActivasAsync()
    {
        await using var _db = await _ctxFactory.CreateDbContextAsync();
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
        await using var _db = await _ctxFactory.CreateDbContextAsync();
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
