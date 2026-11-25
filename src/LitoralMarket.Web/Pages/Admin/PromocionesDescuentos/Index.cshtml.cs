using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages.Admin.PromocionesDescuentos;

/// <summary>
/// Listado de promociones por cantidad (NxM) y por porcentaje. Mismas tablas que el ABM de Comercial Web: lo que se carga
/// aca se aplica alla y viceversa. Solo se administra con promociones/descuentos = 1 y las tablas creadas.
/// </summary>
[Authorize(Roles = "admin")]
public class IndexModel : PageModel
{
    private readonly IPromocionesService _promos;

    public IndexModel(IPromocionesService promos) => _promos = promos;

    public bool Habilitado { get; private set; }
    public bool TablasListas { get; private set; }
    public List<PromoReglaListDto> Reglas { get; private set; } = new();

    public async Task OnGetAsync()
    {
        TablasListas = await _promos.TablasListasAsync();
        Habilitado   = await _promos.HabilitadoAsync();
        if (Habilitado) Reglas = await _promos.ListarAsync();
    }

    // ── POST (token en header X-XSRF-TOKEN) ─────────────────────────────────
    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        if (!await _promos.HabilitadoAsync()) return NoHabilitado();
        try { return new JsonResult(new { ok = true, activa = await _promos.ToggleActivaAsync(id) }); }
        catch (InvalidOperationException ex) { return new JsonResult(new { ok = false, error = ex.Message }); }
    }

    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        if (!await _promos.HabilitadoAsync()) return NoHabilitado();
        await _promos.EliminarAsync(id);
        return new JsonResult(new { ok = true });
    }

    private static JsonResult NoHabilitado() => new(new { ok = false, error = "Las promociones no están habilitadas." });
}
