using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Helpers;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LitoralMarket.Infrastructure.Services;

public class ProductosInicioService : IProductosInicioService
{
    private readonly AppDbContext _db;
    private readonly IParametrosService _parametros;
    private readonly ILogger<ProductosInicioService> _log;

    public ProductosInicioService(AppDbContext db, IParametrosService parametros, ILogger<ProductosInicioService> log)
    {
        _db = db;
        _parametros = parametros;
        _log = log;
    }

    public async Task<List<ProductoInicioItemDto>> ObtenerSeleccionAsync()
    {
        var mostrarSinStock = await _parametros.MostrarSinStockAsync();

        // Proyección sin el blob de imagen.
        var filas = await (
            from s in _db.ProductosInicio.AsNoTracking()
            join p in _db.Productos.AsNoTracking() on s.FkProducto equals p.Id
            orderby s.Orden
            select new
            {
                p.Id, p.CodProveedor, p.CodBarras, p.Descripcion, s.Orden,
                Stock = p.Stock != null ? (p.Stock.Cantidad ?? 0m) : 0m,
                Baja  = p.Baja == true
            }).ToListAsync();

        return filas.Select(f =>
        {
            var sinStock = !mostrarSinStock && f.Stock <= 0;
            return new ProductoInicioItemDto
            {
                ProductoId   = f.Id,
                CodProveedor = f.CodProveedor,
                CodBarras    = f.CodBarras,
                Descripcion  = f.Descripcion ?? string.Empty,
                Orden        = f.Orden,
                Stock        = f.Stock,
                Baja         = f.Baja,
                Visible      = !f.Baja && !sinStock,
                Motivo       = f.Baja ? "Dado de baja" : sinStock ? "Sin stock" : null
            };
        }).ToList();
    }

    public async Task<GuardarSeleccionInicioResultado> GuardarSeleccionAsync(IReadOnlyList<int> ids)
    {
        if (!await _parametros.SeleccionManualInicioHabilitadaAsync())
            return GuardarSeleccionInicioResultado.Fallo("La selección manual del Inicio está desactivada (parámetro ProductosInicioConfigurables).");
        if (ids is null)
            return GuardarSeleccionInicioResultado.Fallo("Lista inválida.");
        if (ids.Count > ProductosInicio.Maximo)
            return GuardarSeleccionInicioResultado.Fallo($"Se pueden seleccionar como máximo {ProductosInicio.Maximo} productos.");
        if (ids.Any(i => i <= 0) || ids.Distinct().Count() != ids.Count)
            return GuardarSeleccionInicioResultado.Fallo("La lista tiene productos inválidos o repetidos.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Los productos que ya estaban elegidos se conservan aunque hoy no sean visibles
            // (los resuelve el administrador); los que se agregan deben existir y no estar de baja.
            var actuales = (await _db.ProductosInicio.Select(s => s.FkProducto).ToListAsync()).ToHashSet();
            var nuevos = ids.Where(i => !actuales.Contains(i)).ToList();
            if (nuevos.Count > 0)
            {
                var validos = await _db.Productos
                    .Where(p => nuevos.Contains(p.Id) && p.Baja != true)
                    .Select(p => p.Id)
                    .ToListAsync();
                var invalidos = nuevos.Except(validos).ToList();
                if (invalidos.Count > 0)
                {
                    await tx.RollbackAsync();
                    return GuardarSeleccionInicioResultado.Fallo(
                        $"No se pueden agregar productos inexistentes o dados de baja (id: {string.Join(", ", invalidos)}).");
                }
            }

            await _db.ProductosInicio.ExecuteDeleteAsync();
            var ahora = DateTime.Now;
            for (var i = 0; i < ids.Count; i++)
                _db.ProductosInicio.Add(new ProductoInicio { FkProducto = ids[i], Orden = i + 1, FechaAlta = ahora });
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return GuardarSeleccionInicioResultado.Correcto();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _log.LogError(ex, "Error al guardar la selección de productos del Inicio");
            return GuardarSeleccionInicioResultado.Fallo("No se pudo guardar la selección. No se realizaron cambios.");
        }
    }
}
