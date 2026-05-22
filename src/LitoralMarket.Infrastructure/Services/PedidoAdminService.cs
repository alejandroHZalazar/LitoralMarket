using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Services;

public class PedidoAdminService : IPedidoAdminService
{
    private readonly AppDbContext  _db;
    private readonly IPedidoService _pedidoSvc;

    public PedidoAdminService(AppDbContext db, IPedidoService pedidoSvc)
    {
        _db        = db;
        _pedidoSvc = pedidoSvc;
    }

    // ──────────────────────────────────────────────────────────────
    // Listado con filtros
    // ──────────────────────────────────────────────────────────────
    public async Task<List<PedidoAdminDto>> ListarAsync(
        DateTime? desde,
        DateTime? hasta,
        string?   estado,
        int       take = 20)
    {
        var q = _db.Pedidos
            .Include(p => p.Cliente)
            .Include(p => p.Detalles)
            .Where(p => p.EsEcommerce == true
                     && p.EstadoEcommerce != "borrador");

        if (desde.HasValue)
            q = q.Where(p => p.Fecha >= desde.Value);

        if (hasta.HasValue)
            q = q.Where(p => p.Fecha <= hasta.Value.AddDays(1).AddTicks(-1));

        if (!string.IsNullOrWhiteSpace(estado) && estado != "todos")
            q = q.Where(p => p.EstadoEcommerce == estado);

        var pedidos = await q
            .OrderByDescending(p => p.Fecha)
            .Take(take)
            .ToListAsync();

        // Método de pago: consultar el cobro más reciente de cada pedido
        var ids    = pedidos.Select(p => p.Id).ToList();
        var cobros = await _db.CobrosEcommerce
            .Where(c => ids.Contains(c.FkPedido))
            .GroupBy(c => c.FkPedido)
            .Select(g => new { FkPedido = g.Key, Tipo = g.OrderByDescending(c => c.FechaCreacion).First().Tipo })
            .ToListAsync();
        var cobroDict = cobros.ToDictionary(c => c.FkPedido, c => c.Tipo);

        return pedidos.Select(p =>
        {
            var nombre = p.FkCliente.HasValue
                ? (p.Cliente?.NombreComercial ?? p.NombreCliente ?? string.Empty)
                : (p.NombreCliente ?? string.Empty);

            return new PedidoAdminDto
            {
                Id               = p.Id,
                Fecha            = p.Fecha ?? DateTime.Now,
                NombreCliente    = nombre,
                EmailCliente     = p.FkCliente.HasValue ? p.Cliente?.Email : p.EmailCliente,
                TelefonoCliente  = p.TelefonoCliente,
                Total            = p.Total ?? 0,
                Estado           = p.EstadoEcommerce,
                DireccionEntrega = p.DireccionEntrega,
                Observacion      = p.Observacion,
                CantItems        = p.Detalles.Count,
                MetodoPago       = cobroDict.GetValueOrDefault(p.Id)
            };
        }).ToList();
    }

    // ──────────────────────────────────────────────────────────────
    // Detalle completo
    // ──────────────────────────────────────────────────────────────
    public async Task<PedidoAdminDto?> ObtenerDetalleAsync(int id)
    {
        var p = await _db.Pedidos
            .Include(x => x.Cliente)
            .Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p is null) return null;

        var cobro = await _db.CobrosEcommerce
            .Where(c => c.FkPedido == id)
            .OrderByDescending(c => c.FechaCreacion)
            .FirstOrDefaultAsync();

        var nombre = p.FkCliente.HasValue
            ? (p.Cliente?.NombreComercial ?? p.NombreCliente ?? string.Empty)
            : (p.NombreCliente ?? string.Empty);

        return new PedidoAdminDto
        {
            Id               = p.Id,
            Fecha            = p.Fecha ?? DateTime.Now,
            NombreCliente    = nombre,
            EmailCliente     = p.FkCliente.HasValue ? p.Cliente?.Email : p.EmailCliente,
            TelefonoCliente  = p.TelefonoCliente,
            Total            = p.Total ?? 0,
            Estado           = p.EstadoEcommerce,
            DireccionEntrega = p.DireccionEntrega,
            Observacion      = p.Observacion,
            CantItems        = p.Detalles.Count,
            MetodoPago       = cobro?.Tipo,
            Items            = p.Detalles.Select(d => new PedidoDetalleDto
            {
                Descripcion = d.Descripcion ?? string.Empty,
                Cantidad    = d.Cantidad    ?? 0,
                Precio      = d.PrecioConIva ?? 0,
                Subtotal    = d.Subtotal    ?? 0
            }).ToList()
        };
    }

    // ──────────────────────────────────────────────────────────────
    // Confirmación manual — reutiliza el mismo flujo que MercadoPago
    // ──────────────────────────────────────────────────────────────
    public async Task ConfirmarManualAsync(int id) =>
        await _pedidoSvc.ConfirmarPagoAsync(id);
}
