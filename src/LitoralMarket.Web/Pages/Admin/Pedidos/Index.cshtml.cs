using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages.Admin.Pedidos;

[Authorize(Roles = "admin")]
public class IndexModel : PageModel
{
    private readonly IPedidoAdminService _pedidos;
    public IndexModel(IPedidoAdminService pedidos) => _pedidos = pedidos;

    public List<PedidoAdminDto> Pedidos { get; private set; } = new();

    // ── GET: carga inicial — últimos 20 pedidos ──────────────────
    public async Task OnGetAsync()
    {
        Pedidos = await _pedidos.ListarAsync(null, null, null, 20);
    }

    // ── AJAX: buscar con filtros ─────────────────────────────────
    public async Task<IActionResult> OnGetBuscarAsync(
        string? desde,
        string? hasta,
        string? estado)
    {
        DateTime? dDesde = DateTime.TryParse(desde, out var d) ? d : null;
        DateTime? dHasta = DateTime.TryParse(hasta, out var h) ? h : null;

        var lista = await _pedidos.ListarAsync(dDesde, dHasta, estado, 200);
        return Partial("_TablaPedidos", lista);
    }

    // ── AJAX: detalle HTML ───────────────────────────────────────
    public async Task<IActionResult> OnGetDetalleAsync(int id)
    {
        var p = await _pedidos.ObtenerDetalleAsync(id);
        if (p is null) return Content("<p class=\"text-danger\">Pedido no encontrado.</p>", "text/html");

        var estadoBadge = p.Estado switch
        {
            "confirmado"    => "<span class=\"badge bg-success\">Confirmado</span>",
            "pendiente_pago"=> "<span class=\"badge bg-warning text-dark\">Pendiente de pago</span>",
            "cancelado"     => "<span class=\"badge bg-danger\">Cancelado</span>",
            _               => $"<span class=\"badge bg-secondary\">{p.Estado}</span>"
        };

        var metodoIcon = p.MetodoPago switch
        {
            "mercadopago" => "<i class=\"bi bi-credit-card me-1\"></i>MercadoPago",
            "transferencia"=> "<i class=\"bi bi-bank me-1\"></i>Transferencia",
            _             => "<i class=\"bi bi-question-circle me-1\"></i>Sin método registrado"
        };

        var itemsHtml = string.Join("", p.Items.Select(i =>
            $"<tr><td class=\"ps-0 small\">{System.Net.WebUtility.HtmlEncode(i.Descripcion)}</td>" +
            $"<td class=\"text-center small\">x{i.Cantidad:N2}</td>" +
            $"<td class=\"text-end small\">${i.Subtotal:N2}</td></tr>"));

        var botonConfirmar = p.Estado == "pendiente_pago"
            ? $"<button class=\"btn btn-success btn-sm w-100 mt-3\" onclick=\"confirmarPedido({p.Id})\">" +
              $"<i class=\"bi bi-check-circle me-1\"></i>Confirmar pedido</button>"
            : "";

        var html = $@"
<div class=""p-2"">
  <div class=""d-flex align-items-center justify-content-between mb-2"">
    <h6 class=""fw-bold mb-0"">Pedido #{p.Id}</h6>
    {estadoBadge}
  </div>
  <p class=""text-muted small mb-1""><i class=""bi bi-calendar3 me-1""></i>{p.Fecha:dd/MM/yyyy HH:mm}</p>
  <hr class=""my-2""/>

  <div class=""small mb-2"">
    <div><i class=""bi bi-person me-1 text-muted""></i><strong>{System.Net.WebUtility.HtmlEncode(p.NombreCliente)}</strong></div>
    {(string.IsNullOrEmpty(p.EmailCliente)    ? "" : $"<div class=\"text-muted\"><i class=\"bi bi-envelope me-1\"></i>{System.Net.WebUtility.HtmlEncode(p.EmailCliente)}</div>")}
    {(string.IsNullOrEmpty(p.TelefonoCliente) ? "" : $"<div class=\"text-muted\"><i class=\"bi bi-telephone me-1\"></i>{System.Net.WebUtility.HtmlEncode(p.TelefonoCliente)}</div>")}
    {(string.IsNullOrEmpty(p.DireccionEntrega)? "" : $"<div class=\"text-muted\"><i class=\"bi bi-geo-alt me-1\"></i>{System.Net.WebUtility.HtmlEncode(p.DireccionEntrega)}</div>")}
  </div>

  <div class=""small text-muted mb-2"">{metodoIcon}</div>

  {(string.IsNullOrEmpty(p.Observacion) ? "" : $"<div class=\"alert alert-light py-1 px-2 small mb-2\"><i class=\"bi bi-chat-left-text me-1\"></i>{System.Net.WebUtility.HtmlEncode(p.Observacion)}</div>")}

  <hr class=""my-2""/>
  <table class=""table table-sm mb-0"">
    <thead><tr>
      <th class=""ps-0 small text-muted fw-normal"">Producto</th>
      <th class=""text-center small text-muted fw-normal"">Cant.</th>
      <th class=""text-end small text-muted fw-normal"">Subtotal</th>
    </tr></thead>
    <tbody>{itemsHtml}</tbody>
    <tfoot><tr>
      <td colspan=""2"" class=""text-end fw-bold ps-0"">Total:</td>
      <td class=""text-end fw-bold text-success"">${p.Total:N2}</td>
    </tr></tfoot>
  </table>
  {botonConfirmar}
</div>";

        return Content(html, "text/html");
    }

    // ── POST: confirmar pedido manualmente ───────────────────────
    public async Task<IActionResult> OnPostConfirmarAsync(int id)
    {
        try
        {
            await _pedidos.ConfirmarManualAsync(id);
            return new JsonResult(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message });
        }
        catch
        {
            return new JsonResult(new { ok = false, error = "Error al confirmar el pedido." });
        }
    }
}
