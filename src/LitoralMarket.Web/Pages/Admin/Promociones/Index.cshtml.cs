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
    private readonly IPromocionesService _promos;

    public IndexModel(IPromocionCarruselService promociones, IParametrosService parametros, IPromocionesService promos)
    {
        _promociones = promociones;
        _parametros = parametros;
        _promos = promos;
    }

    /// <summary>Existe la tabla promocionesCarruselDestinos (sql/carrusel_destinos.sql): se pueden enlazar banners.</summary>
    public bool DestinosDisponibles { get; private set; }
    /// <summary>Módulo de promociones por cantidad / descuento habilitado (promociones/descuentos = 1).</summary>
    public bool PromosHabilitadas { get; private set; }
    /// <summary>Promociones existentes (para elegir el destino y para rotular el actual).</summary>
    public List<PromoReglaListDto> Reglas { get; private set; } = new();

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

        DestinosDisponibles = await _promociones.DestinosDisponiblesAsync();
        PromosHabilitadas   = await _promos.HabilitadoAsync();
        if (PromosHabilitadas) Reglas = await _promos.ListarAsync();
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

    /// <summary>Asocia el banner a una promoción vigente (o le quita el enlace con ReglaId = null).</summary>
    public async Task<IActionResult> OnPostDestinoAsync([FromBody] DestinoRequest req)
    {
        if (req is null) return Resultado(ResultadoPromocion.Fallo("Solicitud inválida."));
        if (req.ReglaId is null) return Resultado(await _promociones.AsignarDestinoAsync(req.Id, null));

        // El id de la regla es lo que se guarda (nunca su nombre). Debe existir, estar vigente y ser del tipo elegido.
        var info = await _promos.ObtenerParaCatalogoAsync(req.ReglaId.Value);
        if (info.Estado != EstadoPromoDestino.Vigente)
            return Resultado(ResultadoPromocion.Fallo("La promoción elegida no existe o no está vigente."));
        if (!string.IsNullOrEmpty(req.Tipo) && !string.Equals(req.Tipo, info.Tipo, StringComparison.OrdinalIgnoreCase))
            return Resultado(ResultadoPromocion.Fallo("La promoción elegida no es del tipo seleccionado."));
        return Resultado(await _promociones.AsignarDestinoAsync(req.Id, req.ReglaId));
    }

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
    public record DestinoRequest(int Id, string? Tipo, int? ReglaId);
}
