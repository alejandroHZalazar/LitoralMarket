using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LitoralMarket.Web.Controllers;

/// <summary>
/// "Registrarse" del modo Credenciales: recibe los datos del visitante y se los envía por
/// email a los administradores (mismos destinatarios que la notificación de pedido nuevo).
/// Todavía NO crea el cliente ni genera credenciales — eso se define aparte.
/// Ruta bajo /api: AccessModeMiddleware la deja pasar sin sesión.
/// </summary>
[Route("api/registro")]
public class RegistroController : ControllerBase
{
    private readonly IParametrosService _params;
    private readonly IEmailService      _email;
    private readonly ILogger<RegistroController> _logger;

    public RegistroController(
        IParametrosService params_, IEmailService email, ILogger<RegistroController> logger)
    {
        _params = params_;
        _email  = email;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("registro")]
    public async Task<IActionResult> Solicitar([FromForm] SolicitudRegistroDto datos)
    {
        // Solo existe en modo Credenciales; en Público el endpoint no existe.
        if (await _params.GetModoAccesoAsync() != "credenciales")
            return NotFound();

        if (!ModelState.IsValid)
        {
            var errores = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct()
                .ToList();
            return BadRequest(new { ok = false, errores });
        }

        bool enviado;
        try
        {
            enviado = await _email.EnviarSolicitudRegistroAsync(datos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registro: excepción enviando la solicitud de '{Nombre}'", datos.NombreComercial);
            enviado = false;
        }

        if (!enviado)
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                ok = false,
                mensaje = "No pudimos enviar su solicitud. Por favor, intente nuevamente en unos minutos."
            });

        var empresa = await _params.GetNombreEmpresaAsync() ?? await _params.GetTituloEcommerceAsync();
        return Ok(new
        {
            ok = true,
            mensaje = $"Sus datos han sido enviados al area de Administración de {empresa}. " +
                      "Dentro de las 48 hs recibirá sus credenciales de acceso. Muchas Gracias!"
        });
    }
}
