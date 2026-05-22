using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Services;

public class ProductoAdminService : IProductoAdminService
{
    private readonly AppDbContext _db;
    public ProductoAdminService(AppDbContext db) => _db = db;

    // ──────────────────────────────────────────────────────────────
    // Búsqueda
    // ──────────────────────────────────────────────────────────────
    public async Task<List<ProductoAdminDto>> BuscarAsync(int tipo, string valor)
    {
        var q = _db.Productos.Include(p => p.Rubro).AsQueryable();

        q = tipo switch
        {
            0 => q.Where(p => p.CodProveedor != null && p.CodProveedor.Contains(valor)),
            1 => q.Where(p => p.CodBarras    != null && p.CodBarras.Contains(valor)),
            _ => q.Where(p => p.Descripcion  != null && p.Descripcion.Contains(valor))
        };

        var lista = await q
            .Where(p => p.Baja != true)
            .OrderBy(p => p.Descripcion)
            .Take(200)
            .ToListAsync();

        return lista.Select(p => new ProductoAdminDto
        {
            Id           = p.Id,
            CodProveedor = p.CodProveedor,
            CodBarras    = p.CodBarras,
            Descripcion  = p.Descripcion,
            RubroNombre  = p.Rubro?.Descripcion
        }).ToList();
    }

    // ──────────────────────────────────────────────────────────────
    // Detalle completo
    // ──────────────────────────────────────────────────────────────
    public async Task<ProductoAdminDto?> ObtenerPorIdAsync(int id)
    {
        var p = await _db.Productos
            .Include(x => x.Rubro)
            .Include(x => x.Stock)
            .Include(x => x.Precio)
            .Include(x => x.Costo)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p is null) return null;

        string? proveedorNombre = null;
        if (p.FkProveedor.HasValue)
            proveedorNombre = await _db.Proveedores
                .Where(pr => pr.Id == p.FkProveedor.Value)
                .Select(pr => pr.NombreComercial)
                .FirstOrDefaultAsync();

        return new ProductoAdminDto
        {
            Id               = p.Id,
            CodProveedor     = p.CodProveedor,
            CodBarras        = p.CodBarras,
            Descripcion      = p.Descripcion,
            DescripcionLarga = p.DescripcionLarga,
            FkRubro          = p.FkRubro,
            RubroNombre      = p.Rubro?.Descripcion,
            FkProveedor      = p.FkProveedor,
            ProveedorNombre  = proveedorNombre,
            Iva              = p.Iva ?? 0,
            PrecioCosto      = p.Costo?.Costo ?? 0,
            PrecioLista      = p.Precio?.Precio ?? 0,
            StockActual      = p.Stock?.Cantidad ?? 0,
            StockMinimo      = p.Stock?.CantidadMinima ?? 0,
            EsPromocion      = p.EsPromocion ?? false,
            Fraccionado      = p.Fraccionado ?? false,
            Dolarizado       = p.Dolarizado  ?? false,
            Baja             = p.Baja        ?? false
        };
    }

    // ──────────────────────────────────────────────────────────────
    // Imagen — lee el blob directamente de la BD
    // ──────────────────────────────────────────────────────────────
    public async Task<byte[]?> ObtenerImagenAsync(int id) =>
        await _db.Productos
            .Where(p => p.Id == id)
            .Select(p => p.Imagen)
            .FirstOrDefaultAsync();

    // ──────────────────────────────────────────────────────────────
    // Alta
    // ──────────────────────────────────────────────────────────────
    public async Task<int> CrearAsync(ProductoAdminDto dto, byte[]? imagen)
    {
        var producto = new Producto
        {
            CodProveedor     = dto.CodProveedor,
            CodBarras        = dto.CodBarras,
            Descripcion      = dto.Descripcion,
            DescripcionLarga = dto.DescripcionLarga,
            FkRubro          = dto.FkRubro,
            FkProveedor      = dto.FkProveedor,
            Iva              = (int?)dto.Iva,
            EsPromocion      = dto.EsPromocion,
            Fraccionado      = dto.Fraccionado,
            Dolarizado       = dto.Dolarizado,
            Baja             = false,
            Imagen           = imagen
        };
        _db.Productos.Add(producto);
        await _db.SaveChangesAsync();

        _db.StockProductos.Add(new StockProducto
        {
            FkProducto     = producto.Id,
            Cantidad       = dto.StockActual,
            CantidadMinima = dto.StockMinimo
        });
        _db.PreciosProductos.Add(new PrecioProducto
        {
            FkProducto = producto.Id,
            Precio     = dto.PrecioLista
        });
        _db.CostosProductos.Add(new CostoProducto
        {
            FkProducto = producto.Id,
            Costo      = dto.PrecioCosto
        });
        await _db.SaveChangesAsync();

        return producto.Id;
    }

    // ──────────────────────────────────────────────────────────────
    // Modificación
    // ──────────────────────────────────────────────────────────────
    public async Task ActualizarAsync(ProductoAdminDto dto, byte[]? imagen)
    {
        var p = await _db.Productos
            .Include(x => x.Stock)
            .Include(x => x.Precio)
            .Include(x => x.Costo)
            .FirstOrDefaultAsync(x => x.Id == dto.Id);

        if (p is null) return;

        p.CodProveedor     = dto.CodProveedor;
        p.CodBarras        = dto.CodBarras;
        p.Descripcion      = dto.Descripcion;
        p.DescripcionLarga = dto.DescripcionLarga;
        p.FkRubro          = dto.FkRubro;
        p.FkProveedor      = dto.FkProveedor;
        p.Iva              = (int?)dto.Iva;
        p.EsPromocion      = dto.EsPromocion;
        p.Fraccionado      = dto.Fraccionado;
        p.Dolarizado       = dto.Dolarizado;
        if (imagen is { Length: > 0 }) p.Imagen = imagen;

        // Stock
        if (p.Stock is null)
            _db.StockProductos.Add(new StockProducto { FkProducto = p.Id, Cantidad = dto.StockActual, CantidadMinima = dto.StockMinimo });
        else
        {
            p.Stock.Cantidad       = dto.StockActual;
            p.Stock.CantidadMinima = dto.StockMinimo;
        }

        // Precio lista
        if (p.Precio is null)
            _db.PreciosProductos.Add(new PrecioProducto { FkProducto = p.Id, Precio = dto.PrecioLista });
        else
            p.Precio.Precio = dto.PrecioLista;

        // Costo
        if (p.Costo is null)
            _db.CostosProductos.Add(new CostoProducto { FkProducto = p.Id, Costo = dto.PrecioCosto });
        else
            p.Costo.Costo = dto.PrecioCosto;

        await _db.SaveChangesAsync();
    }

    // ──────────────────────────────────────────────────────────────
    // Baja lógica
    // ──────────────────────────────────────────────────────────────
    public async Task BajaLogicaAsync(int id)
    {
        var p = await _db.Productos.FindAsync(id);
        if (p is null) return;
        p.Baja = true;
        await _db.SaveChangesAsync();
    }
}
