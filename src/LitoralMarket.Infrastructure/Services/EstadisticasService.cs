using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace LitoralMarket.Infrastructure.Services;

public class EstadisticasService : IEstadisticasService
{
    private readonly AppDbContext _db;
    public EstadisticasService(AppDbContext db) => _db = db;

    public async Task<EstadisticasDto> ObtenerAsync(DateTime desde, DateTime hasta)
    {
        var dto = new EstadisticasDto();

        // Hasta = fin del día (23:59:59.999)
        var hastaFin = hasta.Date.AddDays(1).AddMilliseconds(-1);

        // ── Pedidos ecommerce del período (excluye borradores) ─────────────
        var pedidosPeriodo = _db.Pedidos
            .Where(p => p.EsEcommerce == true
                     && p.EstadoEcommerce != "borrador"
                     && p.Fecha >= desde
                     && p.Fecha <= hastaFin);

        var confirmados = pedidosPeriodo
            .Where(p => p.EstadoEcommerce == "confirmado");

        // ── IDs de pedidos confirmados (para JOIN con detalles) ────────────
        var idsConfirmados = await confirmados.Select(p => p.Id).ToListAsync();

        // ── Detalles de pedidos confirmados ───────────────────────────────
        // Se cargan una sola vez y se reusan para todas las métricas
        var detalles = idsConfirmados.Count == 0
            ? new List<DetalleAgregado>()
            : await _db.PedidoDetalles
                .Where(d => d.FkPedido.HasValue && idsConfirmados.Contains(d.FkPedido.Value))
                .Select(d => new DetalleAgregado
                {
                    FkPedido       = d.FkPedido,
                    FkProducto     = d.FkProducto,
                    Descripcion    = d.Descripcion,
                    Cantidad       = d.Cantidad       ?? 0,
                    Subtotal       = d.Subtotal       ?? 0,
                    SubtotalSinIva = d.SubtotalSinIva ?? 0,
                    Costo          = d.Costo          ?? 0
                })
                .ToListAsync();

        // ── KPIs financieros ───────────────────────────────────────────────
        dto.PedidosConfirmados = idsConfirmados.Count;
        dto.TotalVentas        = detalles.Sum(d => d.Subtotal);
        dto.TotalSinIva        = detalles.Sum(d => d.SubtotalSinIva);
        dto.CostoMercaderia    = detalles.Sum(d => d.Costo * d.Cantidad);
        dto.GananciaBruta      = dto.TotalSinIva - dto.CostoMercaderia;
        dto.MargenPct          = dto.TotalSinIva > 0
                                 ? Math.Round(dto.GananciaBruta / dto.TotalSinIva * 100, 1)
                                 : 0;
        dto.TicketPromedio     = dto.PedidosConfirmados > 0
                                 ? Math.Round(dto.TotalVentas / dto.PedidosConfirmados, 2)
                                 : 0;

        // ── KPIs operativos ────────────────────────────────────────────────
        dto.PendientePago = await pedidosPeriodo
            .CountAsync(p => p.EstadoEcommerce == "pendiente_pago");

        dto.ClientesAutenticados = await confirmados
            .Where(p => p.FkCliente.HasValue)
            .Select(p => p.FkCliente)
            .Distinct()
            .CountAsync();

        dto.ClientesAnonimos = await confirmados
            .Where(p => !p.FkCliente.HasValue && p.GuestToken != null)
            .Select(p => p.GuestToken)
            .Distinct()
            .CountAsync();

        dto.TotalClientesConCuenta = await _db.Clientes
            .CountAsync(c => c.PasswordHash != null && c.Baja != true);

        // ── Distribución por estado ────────────────────────────────────────
        dto.PorEstado = await pedidosPeriodo
            .GroupBy(p => p.EstadoEcommerce)
            .Select(g => new EstadoStatDto
            {
                Estado   = g.Key,
                Cantidad = g.Count(),
                Total    = g.Sum(p => p.Total ?? 0)
            })
            .OrderByDescending(x => x.Cantidad)
            .ToListAsync();

        // ── Top 10 productos con costo y ganancia ─────────────────────────
        dto.TopProductos = detalles
            .Where(d => !string.IsNullOrEmpty(d.Descripcion))
            .GroupBy(d => new { d.FkProducto, d.Descripcion })
            .Select(g =>
            {
                var ventas    = g.Sum(d => d.Subtotal);
                var ventasSIV = g.Sum(d => d.SubtotalSinIva);
                var costo     = g.Sum(d => d.Costo * d.Cantidad);
                var ganancia  = ventasSIV - costo;
                return new ProductoStatDto
                {
                    Descripcion     = g.Key.Descripcion ?? "(sin descripción)",
                    CantidadVendida = g.Sum(d => d.Cantidad),
                    TotalVentas     = ventas,
                    CostoTotal      = costo,
                    GananciaBruta   = ganancia,
                    MargenPct       = ventasSIV > 0
                                      ? Math.Round(ganancia / ventasSIV * 100, 1)
                                      : 0
                };
            })
            .OrderByDescending(x => x.TotalVentas)
            .Take(10)
            .ToList();

        // ── Top 10 clientes autenticados ───────────────────────────────────
        dto.TopRegistrados = await confirmados
            .Where(p => p.FkCliente.HasValue)
            .GroupBy(p => new { p.FkCliente, p.Cliente!.NombreComercial, p.Cliente.RazonSocial })
            .Select(g => new ClienteStatDto
            {
                Nombre          = g.Key.NombreComercial ?? g.Key.RazonSocial
                                  ?? $"Cliente #{g.Key.FkCliente}",
                EsAnonimo       = false,
                CantidadPedidos = g.Count(),
                TotalCompras    = g.Sum(p => p.Total ?? 0)
            })
            .OrderByDescending(x => x.TotalCompras)
            .Take(10)
            .ToListAsync();

        // ── Top 10 clientes anónimos ───────────────────────────────────────
        var anonRaw = await confirmados
            .Where(p => !p.FkCliente.HasValue && p.GuestToken != null)
            .GroupBy(p => new { p.GuestToken, p.NombreCliente })
            .Select(g => new
            {
                g.Key.GuestToken,
                g.Key.NombreCliente,
                CantidadPedidos = g.Count(),
                TotalCompras    = g.Sum(p => p.Total ?? 0)
            })
            .OrderByDescending(x => x.TotalCompras)
            .Take(10)
            .ToListAsync();

        dto.TopAnonimos = anonRaw.Select(g => new ClienteStatDto
        {
            Nombre          = string.IsNullOrWhiteSpace(g.NombreCliente)
                              ? $"Anónimo ({(g.GuestToken != null ? g.GuestToken[..Math.Min(8, g.GuestToken.Length)] : "?")}…)"
                              : g.NombreCliente,
            EsAnonimo       = true,
            CantidadPedidos = g.CantidadPedidos,
            TotalCompras    = g.TotalCompras
        }).ToList();

        // ── Pedidos confirmados por mes (últimos 12 meses, siempre fijo) ───
        var inicioTendencia = new DateTime(DateTime.Now.AddMonths(-11).Year,
                                           DateTime.Now.AddMonths(-11).Month, 1);

        var rawMes = await _db.Pedidos
            .Where(p => p.EsEcommerce == true
                     && p.EstadoEcommerce == "confirmado"
                     && p.Fecha >= inicioTendencia)
            .GroupBy(p => new { p.Fecha!.Value.Year, p.Fecha.Value.Month })
            .Select(g => new
            {
                g.Key.Year, g.Key.Month,
                Cantidad = g.Count(),
                Total    = g.Sum(p => p.Total ?? 0)
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToListAsync();

        var meses = new List<MesStatDto>();
        for (int i = 0; i < 12; i++)
        {
            var mes   = inicioTendencia.AddMonths(i);
            var found = rawMes.FirstOrDefault(r => r.Year == mes.Year && r.Month == mes.Month);
            meses.Add(new MesStatDto
            {
                Label    = mes.ToString("MMM yy", CultureInfo.GetCultureInfo("es-AR")),
                Cantidad = found?.Cantidad ?? 0,
                Total    = found?.Total    ?? 0
            });
        }
        dto.PorMes = meses;

        return dto;
    }

    // ── DTO interno para manipulación en memoria ───────────────────────────
    private class DetalleAgregado
    {
        public int?    FkPedido       { get; set; }
        public int?    FkProducto     { get; set; }
        public string? Descripcion    { get; set; }
        public decimal Cantidad       { get; set; }
        public decimal Subtotal       { get; set; }
        public decimal SubtotalSinIva { get; set; }
        public decimal Costo          { get; set; }
    }
}
