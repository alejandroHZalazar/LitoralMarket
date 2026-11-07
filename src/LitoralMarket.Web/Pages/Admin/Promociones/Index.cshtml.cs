using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages.Admin.Promociones;

[Authorize(Roles = "admin")]
public class IndexModel : PageModel
{
    private readonly IPromocionCarruselService _promociones;
    private readonly IParametrosService _parametros;

    public IndexModel(IPromocionCarruselService promociones, IParametrosService parametros)
    {
        _promociones = promociones;
        _parametros = parametros;
    }

    public List<PromocionCarruselDto> Promociones { get; private set; } = new();
    /// <summary>ecommerce/CarruselPromociones == "1" (solo informativo en esta pantalla).</summary>
    public bool CarruselActivo { get; private set; }
    public int IntervaloSegundos { get; private set; } = 5;
    public int MaxMb => PromocionCarruselService.MaxBytes / (1024 * 1024);

    public async Task OnGetAsync()
    {
        Promociones    = await _promociones.ListarAsync();
        CarruselActivo = await _parametros.MostrarCarruselPromocionesAsync();
        IntervaloSegundos = await _parametros.GetIntervaloCarruselPromocionesAsync();
    }

    // ── POST (FormData / JSON + token en header X-XSRF-TOKEN) ──────────────
    public async Task<IActionResult> OnPostCrearAsync(IFormFile? imagen, string? titulo)
    {
        var bytes = await LeerAsync(imagen);
        return Resultado(bytes is null
            ? ResultadoPromocion.Fallo("Elegí una imagen.")
            : await _promociones.CrearAsync(bytes, titulo));
    }

    public async Task<IActionResult> OnPostReemplazarAsync(int id, IFormFile? imagen)
    {
        var bytes = await LeerAsync(imagen);
        return Resultado(bytes is null
            ? ResultadoPromocion.Fallo("Elegí una imagen.")
            : await _promociones.ReemplazarImagenAsync(id, bytes));
    }

    public async Task<IActionResult> OnPostTituloAsync([FromBody] TituloRequest req) =>
        Resultado(await _promociones.ActualizarTituloAsync(req.Id, req.Titulo));

    public async Task<IActionResult> OnPostEliminarAsync([FromBody] IdRequest req) =>
        Resultado(await _promociones.EliminarAsync(req.Id));

    public async Task<IActionResult> OnPostReordenarAsync([FromBody] OrdenRequest req) =>
        Resultado(await _promociones.ReordenarAsync(req?.Ids ?? new List<int>()));

    // ── Helpers ────────────────────────────────────────────────────────────
    private static async Task<byte[]?> LeerAsync(IFormFile? archivo)
    {
        // Se lee como máximo MaxBytes + 1 para que el servicio detecte el exceso sin cargar archivos enormes.
        if (archivo is null || archivo.Length == 0) return null;
        if (archivo.Length > PromocionCarruselService.MaxBytes) return new byte[PromocionCarruselService.MaxBytes + 1];
        using var ms = new MemoryStream((int)archivo.Length);
        await archivo.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static JsonResult Resultado(ResultadoPromocion r) => new(new { ok = r.Ok, error = r.Error, id = r.Id });

    public record IdRequest(int Id);
    public record TituloRequest(int Id, string? Titulo);
    public record OrdenRequest(List<int> Ids);
}
