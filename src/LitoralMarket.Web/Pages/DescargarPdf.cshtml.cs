using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LitoralMarket.Web.Pages;

/// <summary>
/// Genera el comprobante PDF en memoria bajo demanda y lo sirve como descarga.
/// El archivo nunca se almacena en disco ni en base de datos.
/// </summary>
public class DescargarPdfModel : PageModel
{
    private readonly IPdfPagoService         _pdf;
    private readonly AppDbContext            _db;
    private readonly ILogger<DescargarPdfModel> _logger;

    public DescargarPdfModel(IPdfPagoService pdf, AppDbContext db, ILogger<DescargarPdfModel> logger)
    {
        _pdf    = pdf;
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// GET /descargar-pdf?pedidoId=123
    /// </summary>
    public async Task<IActionResult> OnGetAsync(int pedidoId)
    {
        if (pedidoId <= 0)
            return BadRequest("pedidoId inválido.");

        // ── Verificación de propiedad (IDOR) ────────────────────────────────
        // Los administradores pueden descargar cualquier comprobante.
        // Los demás usuarios solo pueden descargar sus propios pedidos.
        if (!User.IsInRole("admin"))
        {
            var pedidoCheck = await _db.Pedidos
                .AsNoTracking()
                .Select(p => new { p.Id, p.FkCliente, p.GuestToken })
                .FirstOrDefaultAsync(p => p.Id == pedidoId);

            if (pedidoCheck is null)
                return NotFound("Pedido no encontrado.");

            var clienteIdClaim = User.FindFirst("clienteId");
            if (clienteIdClaim is not null && int.TryParse(clienteIdClaim.Value, out var cid))
            {
                // Usuario autenticado: debe ser el dueño del pedido
                if (pedidoCheck.FkCliente != cid)
                    return Forbid();
            }
            else
            {
                // Usuario anónimo: verificar por guest token en cookie
                Request.Cookies.TryGetValue("litoral_guest", out var guest);
                if (string.IsNullOrEmpty(guest) || pedidoCheck.GuestToken != guest)
                    return Forbid();
            }
        }

        // Buscar el cobro más reciente del pedido
        var cobro = await _db.CobrosEcommerce
            .Where(c => c.FkPedido == pedidoId)
            .OrderByDescending(c => c.FechaCreacion)
            .FirstOrDefaultAsync();

        try
        {
            byte[] bytes;
            if (cobro is not null)
                bytes = await _pdf.GenerarComprobanteAsync(pedidoId, cobro.Id);
            else
                bytes = await _pdf.GenerarComprobanteSinPagoAsync(pedidoId);

            var nombre = $"comprobante-pedido-{pedidoId:D6}.pdf";
            return File(bytes, "application/pdf", nombre);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando PDF para pedido #{PedidoId}", pedidoId);
            return StatusCode(500, "No se pudo generar el comprobante. Intentá nuevamente.");
        }
    }
}
