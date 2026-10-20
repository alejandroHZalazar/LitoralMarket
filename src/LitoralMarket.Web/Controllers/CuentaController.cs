using System.Security.Claims;
using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Helpers;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LitoralMarket.Web.Controllers;

/// <summary>
/// Acciones de cuenta del usuario autenticado (cliente o administrador).
/// El usuario se identifica SOLO por los claims de su sesión — nunca por datos del form —
/// así nadie puede cambiar la contraseña de otra cuenta.
/// </summary>
[Route("api/cuenta")]
public class CuentaController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ILogger<CuentaController> _logger;

    public CuentaController(IAuthService auth, ILogger<CuentaController> logger)
    {
        _auth   = auth;
        _logger = logger;
    }

    // Sin [Authorize]: el desafío de la cookie redirigiría a /login (HTML) y el fetch
    // del modal espera JSON. Se valida la sesión acá y se responde 401.
    [HttpPost("cambiar-password")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("cambiarPassword")]
    public async Task<IActionResult> CambiarPassword([FromForm] CambioPasswordDto datos)
    {
        if (User.Identity?.IsAuthenticated != true)
            return SesionInvalida();

        if (string.IsNullOrEmpty(datos.Actual))
            return BadRequest(new { ok = false, errores = new[] { "Ingresá tu contraseña actual." } });

        var errores = PasswordPolicyHelper.Validar(datos.Nueva, datos.Confirmacion);
        if (errores.Count > 0)
            return BadRequest(new { ok = false, errores });

        // Tipo e id salen de los claims emitidos en el login (Login.cshtml.cs).
        ResultadoCambioPassword resultado;
        if (User.IsInRole("admin") && int.TryParse(User.FindFirstValue("usuarioId"), out var usuarioId))
            resultado = await _auth.CambiarPasswordAdminAsync(usuarioId, datos.Actual, datos.Nueva!);
        else if (int.TryParse(User.FindFirstValue("clienteId"), out var clienteId))
            resultado = await _auth.CambiarPasswordClienteAsync(clienteId, datos.Actual, datos.Nueva!);
        else
            return SesionInvalida();

        switch (resultado)
        {
            case ResultadoCambioPassword.Ok:
                _logger.LogInformation("Cuenta: contraseña cambiada para {Tipo} {Id}",
                    User.IsInRole("admin") ? "admin" : "cliente", User.FindFirstValue(ClaimTypes.NameIdentifier));
                return Ok(new { ok = true, mensaje = "Tu contraseña fue actualizada correctamente." });

            case ResultadoCambioPassword.ActualIncorrecta:
                return BadRequest(new { ok = false, errores = new[] { "La contraseña actual es incorrecta." } });

            default:
                return SesionInvalida();
        }
    }

    private IActionResult SesionInvalida() =>
        StatusCode(StatusCodes.Status401Unauthorized, new
        {
            ok = false,
            mensaje = "Tu sesión no es válida o expiró. Volvé a iniciar sesión."
        });
}
