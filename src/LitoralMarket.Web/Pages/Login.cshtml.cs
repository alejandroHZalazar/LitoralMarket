using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace LitoralMarket.Web.Pages;

[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
public class LoginPageModel : PageModel
{
    private readonly IAuthService       _auth;
    private readonly IParametrosService _params;

    public LoginPageModel(IAuthService auth, IParametrosService params_)
    {
        _auth   = auth;
        _params = params_;
    }

    // ── Datos de formulario ──────────────────────────────────────
    [BindProperty] public LoginDto Datos          { get; set; } = new();
    [BindProperty] public string   NombreAdmin    { get; set; } = string.Empty;
    [BindProperty] public bool     EsAdministrador { get; set; }

    // ── Estado de la página ──────────────────────────────────────
    public string ModoAcceso { get; private set; } = "publico";
    public string Error      { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Index");

        ModoAcceso = await _params.GetModoAccesoAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        ModoAcceso = await _params.GetModoAccesoAsync();

        // En modo "publico" el admin es el único que puede iniciar sesión
        var loginComoAdmin = EsAdministrador || ModoAcceso == "publico";

        if (loginComoAdmin)
        {
            // Limpiar errores de validación del campo Email (no aplica para admin)
            ModelState.Remove(nameof(Datos) + "." + nameof(Datos.Email));

            if (string.IsNullOrWhiteSpace(NombreAdmin))
            {
                Error = "El nombre de usuario es obligatorio.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Datos.Password))
            {
                Error = "La contraseña es obligatoria.";
                return Page();
            }

            var admin = await _auth.ValidarAdminAsync(NombreAdmin.Trim(), Datos.Password);
            if (admin is null)
            {
                Error = "Usuario o contraseña incorrectos.";
                return Page();
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
                new(ClaimTypes.Role,           "admin"),
                new("usuarioId",               admin.Id.ToString()),
                new("nombre",                  admin.Nombre ?? "Administrador"),
                new("esAdmin",                 "true")
            };

            await SignInAsync(claims, Datos.RecordarMe);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            return RedirectToPage("/Index");
        }

        // ── Login de cliente ──────────────────────────────────────
        if (!ModelState.IsValid)
            return Page();

        // Trim del email: [EmailAddress] no rechaza espacios al inicio/final,
        // y una comparación exacta contra la BD (c.Email == email) fallaría en
        // silencio si el valor viene con espacios (ej. copiado y pegado).
        var cliente = await _auth.ValidarCredencialesAsync(Datos.Email.Trim(), Datos.Password);
        if (cliente is null)
        {
            Error = "Email o contraseña incorrectos.";
            return Page();
        }

        var clienteClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, cliente.Id.ToString()),
            new(ClaimTypes.Email,          cliente.Email ?? string.Empty),
            new("clienteId",               cliente.Id.ToString()),
            new("nombre",                  cliente.NombreComercial ?? cliente.RazonSocial ?? cliente.Email ?? string.Empty)
        };

        await SignInAsync(clienteClaims, Datos.RecordarMe);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToPage("/Index");
    }

    // ─────────────────────────────────────────────────────────────
    private async Task SignInAsync(List<Claim> claims, bool recordar)
    {
        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var props     = new AuthenticationProperties
        {
            IsPersistent = recordar,
            ExpiresUtc   = recordar
                ? DateTimeOffset.UtcNow.AddDays(30)
                : DateTimeOffset.UtcNow.AddHours(8)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);
    }
}
