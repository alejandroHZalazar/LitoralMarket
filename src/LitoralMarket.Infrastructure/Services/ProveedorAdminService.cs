using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Services;

public class ProveedorAdminService : IProveedorAdminService
{
    private readonly AppDbContext _db;
    public ProveedorAdminService(AppDbContext db) => _db = db;

    // ──────────────────────────────────────────────────────────────
    // Búsqueda (incluye dados de baja para poder recuperarlos)
    // ──────────────────────────────────────────────────────────────
    public async Task<List<ProveedorAdminDto>> BuscarAsync(string valor)
    {
        var q = _db.Proveedores.AsQueryable();

        if (!string.IsNullOrWhiteSpace(valor))
            q = q.Where(p => p.NombreComercial != null && p.NombreComercial.Contains(valor)
                           || p.Cuil           != null && p.Cuil.Contains(valor));

        return await q
            .Where(p => p.Baja != true)
            .OrderBy(p => p.NombreComercial)
            .Take(200)
            .Select(p => new ProveedorAdminDto
            {
                Id              = p.Id,
                NombreComercial = p.NombreComercial,
                Cuil            = p.Cuil,
                Email           = p.Email,
                Telefono        = p.Telefono,
                Celular         = p.Celular,
                Ganancia        = p.Ganancia  ?? 0,
                Descuento       = p.Descuento ?? 0,
                Baja            = p.Baja      ?? false
            })
            .ToListAsync();
    }

    // ──────────────────────────────────────────────────────────────
    // Detalle
    // ──────────────────────────────────────────────────────────────
    public async Task<ProveedorAdminDto?> ObtenerPorIdAsync(int id)
    {
        var p = await _db.Proveedores.FindAsync(id);
        if (p is null) return null;

        return new ProveedorAdminDto
        {
            Id              = p.Id,
            NombreComercial = p.NombreComercial,
            Cuil            = p.Cuil,
            Direccion       = p.Direccion,
            Email           = p.Email,
            Telefono        = p.Telefono,
            Celular         = p.Celular,
            Ganancia        = p.Ganancia  ?? 0,
            Descuento       = p.Descuento ?? 0,
            Baja            = p.Baja      ?? false
        };
    }

    // ──────────────────────────────────────────────────────────────
    // Alta
    // ──────────────────────────────────────────────────────────────
    public async Task<int> CrearAsync(ProveedorAdminDto dto)
    {
        var proveedor = new Proveedor
        {
            NombreComercial = dto.NombreComercial,
            Cuil            = dto.Cuil,
            Direccion       = dto.Direccion,
            Email           = dto.Email,
            Telefono        = dto.Telefono,
            Celular         = dto.Celular,
            Ganancia        = dto.Ganancia,
            Descuento       = dto.Descuento,
            Baja            = false
        };
        _db.Proveedores.Add(proveedor);
        await _db.SaveChangesAsync();
        return proveedor.Id;
    }

    // ──────────────────────────────────────────────────────────────
    // Modificación
    // ──────────────────────────────────────────────────────────────
    public async Task ActualizarAsync(ProveedorAdminDto dto)
    {
        var p = await _db.Proveedores.FindAsync(dto.Id);
        if (p is null) return;

        p.NombreComercial = dto.NombreComercial;
        p.Cuil            = dto.Cuil;
        p.Direccion       = dto.Direccion;
        p.Email           = dto.Email;
        p.Telefono        = dto.Telefono;
        p.Celular         = dto.Celular;
        p.Ganancia        = dto.Ganancia;
        p.Descuento       = dto.Descuento;

        await _db.SaveChangesAsync();
    }

    // ──────────────────────────────────────────────────────────────
    // Baja lógica
    // ──────────────────────────────────────────────────────────────
    public async Task BajaLogicaAsync(int id)
    {
        var p = await _db.Proveedores.FindAsync(id);
        if (p is null) return;
        p.Baja = true;
        await _db.SaveChangesAsync();
    }
}
