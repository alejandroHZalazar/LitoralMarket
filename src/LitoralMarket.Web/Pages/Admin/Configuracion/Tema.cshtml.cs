using System.Text.RegularExpressions;
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

    public async Task OnGetAsync()
    {
        var raw = Request.Cookies["lm_theme"] ?? "light";
        TemaActual = Array.IndexOf(Validos, raw) >= 0 ? raw : "light";

        var cp = await _parametros.GetColorPrimarioAsync();
        var ca = await _parametros.GetColorAcentoAsync();
        ColoresCustom = EsHex(cp) || EsHex(ca);
        ColorPrimario = EsHex(cp) ? cp! : PrimarioDefault;
        ColorAcento   = EsHex(ca) ? ca! : AcentoDefault;
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

    private static bool EsHex(string? c) =>
        c is not null && Regex.IsMatch(c, "^#[0-9a-fA-F]{6}$");
}
