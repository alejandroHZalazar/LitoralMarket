using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages.Admin.Productos;

/// <summary>
/// Maestros de medidas y colores para productos con variantes. Solo disponible con
/// ecommerce/HabilitarVariantesProducto = 1 y las tablas creadas (sql/variantes_01_tablas.sql).
/// </summary>
[Authorize(Roles = "admin")]
public class VariantesModel : PageModel
{
    private readonly IVarianteAdminService _variantes;
    private readonly IParametrosService _parametros;

    public VariantesModel(IVarianteAdminService variantes, IParametrosService parametros)
    {
        _variantes = variantes;
        _parametros = parametros;
    }

    public bool Habilitado { get; private set; }
    public bool ParametroActivo { get; private set; }
    public List<AtributoVarianteDto> Medidas { get; private set; } = new();
    public List<AtributoVarianteDto> Colores { get; private set; } = new();

    public async Task OnGetAsync()
    {
        ParametroActivo = await _parametros.HabilitarVariantesProductoAsync();
        Habilitado = ParametroActivo && await _variantes.TablasListasAsync();
        if (!Habilitado) return;
        Medidas = await _variantes.ListarMedidasAsync();
        Colores = await _variantes.ListarColoresAsync();
    }

    // ── POST JSON (token en header X-XSRF-TOKEN) ─────────────────────────
    public async Task<IActionResult> OnPostGuardarAsync([FromBody] GuardarRequest req)
    {
        if (!await _parametros.HabilitarVariantesProductoAsync() || req is null)
            return new JsonResult(new { ok = false, error = "Las variantes no están habilitadas." });

        var r = req.Tipo == "color"
            ? await _variantes.GuardarColorAsync(req.Id, req.Nombre ?? "", req.Hex, req.Orden, req.Activa)
            : await _variantes.GuardarMedidaAsync(req.Id, req.Nombre ?? "", req.Orden, req.Activa);
        return new JsonResult(new { ok = r.Ok, error = r.Error, id = r.Id });
    }

    public async Task<IActionResult> OnPostEliminarAsync([FromBody] EliminarRequest req)
    {
        if (!await _parametros.HabilitarVariantesProductoAsync() || req is null)
            return new JsonResult(new { ok = false, error = "Las variantes no están habilitadas." });

        var r = req.Tipo == "color"
            ? await _variantes.EliminarColorAsync(req.Id)
            : await _variantes.EliminarMedidaAsync(req.Id);
        return new JsonResult(new { ok = r.Ok, error = r.Error });
    }

    public record GuardarRequest(string? Tipo, int? Id, string? Nombre, string? Hex, int Orden, bool Activa);
    public record EliminarRequest(string? Tipo, int Id);
}
