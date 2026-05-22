using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

public class PedidoResumenPageModel : PageModel
{
    private readonly IPedidoService    _pedidos;
    private readonly IParametrosService _params;

    public PedidoResumenPageModel(IPedidoService pedidos, IParametrosService parametros)
    {
        _pedidos = pedidos;
        _params  = parametros;
    }

    public PedidoResumenDto? Resumen           { get; private set; }
    public bool              EmailEnviado      { get; private set; }
    public bool              EsModoCredenciales{ get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Resumen = await _pedidos.ObtenerResumenAsync(id);
        if (Resumen is null) return NotFound();

        // Solo mostramos pedidos en estado confirmado o pendiente_pago
        if (Resumen.Estado is not ("confirmado" or "pendiente_pago"))
            return RedirectToPage("/Index");

        // ── Verificación de propiedad (IDOR) ────────────────────────────────
        if (!User.IsInRole("admin"))
        {
            var clienteIdClaim = User.FindFirst("clienteId");
            if (clienteIdClaim is not null && int.TryParse(clienteIdClaim.Value, out var cid))
            {
                if (Resumen.ClienteId != cid)
                    return Forbid();
            }
            else
            {
                Request.Cookies.TryGetValue("litoral_guest", out var guest);
                if (string.IsNullOrEmpty(guest) || Resumen.GuestToken != guest)
                    return Forbid();
            }
        }

        EsModoCredenciales = await _params.GetModoAccesoAsync() == "credenciales";

        // Viene desde el flujo de reembolso si Pago.cshtml.cs lo guardó en TempData
        EmailEnviado = TempData["EmailEnviado"] is bool b && b;

        return Page();
    }
}
