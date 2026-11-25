using System.Text.RegularExpressions;
using LitoralMarket.Application.Helpers;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages.Admin.Configuracion;

[Authorize(Roles = "admin")]
public class TemaModel : PageModel
{
    private static readonly string[] Validos = ["light", "dark", "system", "high-contrast"];

    // Colores por defecto de la marca (paleta original)
    public const string PrimarioDefault = "#1DB062";
    public const string AcentoDefault   = "#C9A84C";

    private readonly IParametrosService _parametros;

    public TemaModel(IParametrosService parametros) => _parametros = parametros;

    public string TemaActual     { get; private set; } = "light";
    public string ColorPrimario  { get; private set; } = PrimarioDefault;
    public string ColorAcento    { get; private set; } = AcentoDefault;
    public bool   ColoresCustom  { get; private set; }

    // ── Apariencia de la tienda (parametros tema/colorNavegacion, colorPromocion, colorFondo, fuenteTitulos, fuenteTexto) ──
    // null = sin personalizar (la tienda usa su apariencia original).
    public const string PromocionDefault = "#C62828";
    public const string FondoDefault     = "#F6FAF7";
    public string? ColorNavegacion { get; private set; }
    public string? ColorPromocion  { get; private set; }
    public string? ColorFondo      { get; private set; }
    public string? FuenteTitulos   { get; private set; }
    public string? FuenteTexto     { get; private set; }
    public bool AparienciaCustom => ColorNavegacion is not null || ColorPromocion is not null || ColorFondo is not null
                                    || FuenteTitulos is not null || FuenteTexto is not null;
    public IReadOnlyList<FuenteTema> FuentesTitulos => TemaVisualHelper.FuentesTitulos;
    public IReadOnlyList<FuenteTema> FuentesTexto   => TemaVisualHelper.FuentesTexto;
    public string FontsourceVersion => TemaVisualHelper.FontsourceVersion;
    /// <summary>Color que hoy tienen la barra y el menu sin personalizar: el primario oscurecido (igual que --verde-profundo).</summary>
    public string NavegacionDefault => TemaVisualHelper.Mezclar(ColorPrimario, "#000000", 0.40);

    public async Task OnGetAsync()
    {
        var raw = Request.Cookies["lm_theme"] ?? "light";
        TemaActual = Array.IndexOf(Validos, raw) >= 0 ? raw : "light";

        var cp = await _parametros.GetColorPrimarioAsync();
        var ca = await _parametros.GetColorAcentoAsync();
        ColoresCustom = EsHex(cp) || EsHex(ca);
        ColorPrimario = EsHex(cp) ? cp! : PrimarioDefault;
        ColorAcento   = EsHex(ca) ? ca! : AcentoDefault;

        var nav = await _parametros.GetValorAsync("tema", "colorNavegacion");
        var pro = await _parametros.GetValorAsync("tema", "colorPromocion");
        var fon = await _parametros.GetValorAsync("tema", "colorFondo");
        ColorNavegacion = EsHex(nav) ? nav!.ToUpperInvariant() : null;
        ColorPromocion  = EsHex(pro) ? pro!.ToUpperInvariant() : null;
        ColorFondo      = EsHex(fon) ? fon!.ToUpperInvariant() : null;
        FuenteTitulos   = TemaVisualHelper.BuscarFuente(TemaVisualHelper.FuentesTitulos, await _parametros.GetValorAsync("tema", "fuenteTitulos"))?.Clave;
        FuenteTexto     = TemaVisualHelper.BuscarFuente(TemaVisualHelper.FuentesTexto,   await _parametros.GetValorAsync("tema", "fuenteTexto"))?.Clave;
    }

    public IActionResult OnPostAplicar(string tema)
    {
        if (Array.IndexOf(Validos, tema) < 0)
            tema = "light";

        var opts = new CookieOptions
        {
            Path     = "/",
            MaxAge   = TimeSpan.FromDays(365),
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure   = Request.IsHttps
        };
        Response.Cookies.Append("lm_theme", tema, opts);

        TempData["TemaAplicado"] = tema;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostColoresAsync(string primario, string acento)
    {
        // Solo se guardan valores hex válidos; si son inválidos se ignora el campo.
        if (EsHex(primario))
            await _parametros.SetValorAsync("tema", "colorPrimario", primario.ToUpperInvariant());
        if (EsHex(acento))
            await _parametros.SetValorAsync("tema", "colorAcento", acento.ToUpperInvariant());

        TempData["ColoresGuardados"] = true;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestaurarColoresAsync()
    {
        // Guardar null elimina el override → vuelven los colores propios de cada tema.
        await _parametros.SetValorAsync("tema", "colorPrimario", null);
        await _parametros.SetValorAsync("tema", "colorAcento", null);

        TempData["ColoresRestaurados"] = true;
        return RedirectToPage();
    }

    /// <summary>
    /// Guarda la apariencia de la tienda. Cada campo que llega vacio o invalido se BORRA (vuelve a su valor original): los
    /// colores opcionales llegan solo si su casilla "Personalizar" esta marcada. El color del texto no se guarda: se calcula
    /// solo segun el fondo (TemaVisualHelper). Las fuentes solo pueden ser las del catalogo.
    /// </summary>
    public async Task<IActionResult> OnPostAparienciaAsync(
        string? navegacion, string? promocion, string? fondo, string? fuenteTitulos, string? fuenteTexto)
    {
        await GuardarColorAsync("colorNavegacion", navegacion);
        await GuardarColorAsync("colorPromocion",  promocion);
        await GuardarColorAsync("colorFondo",      fondo);
        await _parametros.SetValorAsync("tema", "fuenteTitulos",
            TemaVisualHelper.BuscarFuente(TemaVisualHelper.FuentesTitulos, fuenteTitulos)?.Clave);
        await _parametros.SetValorAsync("tema", "fuenteTexto",
            TemaVisualHelper.BuscarFuente(TemaVisualHelper.FuentesTexto, fuenteTexto)?.Clave);

        TempData["AparienciaGuardada"] = true;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestaurarAparienciaAsync()
    {
        foreach (var k in new[] { "colorNavegacion", "colorPromocion", "colorFondo", "fuenteTitulos", "fuenteTexto" })
            await _parametros.SetValorAsync("tema", k, null);

        TempData["AparienciaRestaurada"] = true;
        return RedirectToPage();
    }

    private Task GuardarColorAsync(string parametro, string? valor) =>
        _parametros.SetValorAsync("tema", parametro, EsHex(valor) ? valor!.ToUpperInvariant() : null);

    private static bool EsHex(string? c) =>
        c is not null && Regex.IsMatch(c, "^#[0-9a-fA-F]{6}$");
}
