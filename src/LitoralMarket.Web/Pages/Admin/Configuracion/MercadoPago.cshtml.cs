using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages.Admin.Configuracion;

[Authorize(Roles = "admin")]
public class MercadoPagoModel : PageModel
{
    private readonly IMercadoPagoOAuthService _oauth;

    public MercadoPagoModel(IMercadoPagoOAuthService oauth) => _oauth = oauth;

    public MpConnectionInfoDto Info { get; private set; } = default!;

    // Mensajes de flash (vienen de TempData tras redirect desde el controller)
    public string? MensajeExito { get; private set; }
    public string? MensajeError { get; private set; }

    public async Task OnGetAsync()
    {
        MensajeExito = TempData["Exito"] as string;
        MensajeError = TempData["Error"] as string;
        Info         = await _oauth.GetConnectionInfoAsync();
    }

    // ── POST: Desconectar ─────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostDesconectarAsync()
    {
        var ok = await _oauth.DisconnectAsync();
        TempData[ok ? "Exito" : "Error"] = ok
            ? "Cuenta de Mercado Pago desconectada correctamente."
            : "Ocurrió un error al desconectar. Revisá los logs.";
        return RedirectToPage();
    }
}
