using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace LitoralMarket.Web.Pages.Admin.Estadisticas;

[Authorize(Roles = "admin")]
public class IndexModel : PageModel
{
    private readonly IEstadisticasService _svc;
    public IndexModel(IEstadisticasService svc) => _svc = svc;

    public void OnGet() { }   // la UI se carga vía AJAX

    // ── AJAX — devuelve estadísticas en JSON ──────────────────────────────
    public async Task<IActionResult> OnGetDatosAsync(string? desde, string? hasta)
    {
        // Por defecto: últimos 30 días
        var desdeDt = DateTime.TryParse(desde, out var d) ? d : DateTime.Today.AddDays(-29);
        var hastaDt = DateTime.TryParse(hasta, out var h) ? h : DateTime.Today;

        // Validación: máximo 2 años de rango
        if ((hastaDt - desdeDt).TotalDays > 730)
            desdeDt = hastaDt.AddDays(-730);

        var dto = await _svc.ObtenerAsync(desdeDt, hastaDt);

        return new JsonResult(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
