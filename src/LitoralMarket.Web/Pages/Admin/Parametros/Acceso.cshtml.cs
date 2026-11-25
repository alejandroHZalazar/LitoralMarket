using LitoralMarket.Web.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace LitoralMarket.Web.Pages.Admin.Parametros;

[Authorize(Roles = "admin")]
[EnableRateLimiting("claveParametros")]
public class AccesoModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly ILogger<AccesoModel> _logger;

    public AccesoModel(IConfiguration config, ILogger<AccesoModel> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string? Error { get; private set; }

    public IActionResult OnGet() =>
        AccesoParametros.EstaDesbloqueado(HttpContext) ? RedirectToPage("Index") : Page();

    // El parámetro es anulable a propósito: vacío debe mostrar el mensaje propio, no un error de validación.
    public IActionResult OnPost(string? clave)
    {
        if (!AccesoParametros.Verificar(clave, _config))
        {
            _logger.LogWarning("Parámetros: clave incorrecta (usuario {Usuario}).", User.Identity?.Name);
            Error = "La contraseña no es correcta.";
            return Page();
        }
        AccesoParametros.Desbloquear(HttpContext);
        return RedirectToPage("Index");
    }
}
