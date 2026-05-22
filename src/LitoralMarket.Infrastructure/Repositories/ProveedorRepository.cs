using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Repositories;

public class ProveedorRepository : IProveedorRepository
{
    private readonly AppDbContext _db;
    public ProveedorRepository(AppDbContext db) => _db = db;

    public async Task<List<ProveedorDto>> ObtenerActivosAsync() =>
        await _db.Proveedores
            .Where(p => p.Baja != true)
            .OrderBy(p => p.NombreComercial)
            .Select(p => new ProveedorDto
            {
                Id        = p.Id,
                Nombre    = p.NombreComercial ?? string.Empty,
                Ganancia  = p.Ganancia  ?? 0,
                Descuento = p.Descuento ?? 0
            })
            .ToListAsync();

    public async Task<ProveedorDto?> ObtenerPorIdAsync(int id) =>
        await _db.Proveedores
            .Where(p => p.Id == id)
            .Select(p => new ProveedorDto
            {
                Id        = p.Id,
                Nombre    = p.NombreComercial ?? string.Empty,
                Ganancia  = p.Ganancia  ?? 0,
                Descuento = p.Descuento ?? 0
            })
            .FirstOrDefaultAsync();
}
