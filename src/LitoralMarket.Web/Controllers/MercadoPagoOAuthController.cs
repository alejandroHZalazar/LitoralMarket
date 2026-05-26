using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LitoralMarket.Web.Controllers;

/// <summary>
/// Maneja el flujo OAuth de MercadoPago:
///
///   GET /mp/connect   → genera state, guarda en sesión y redirige a MP
///   GET /mp/callback  → recibe code, valida state, intercambia tokens, persiste
///
/// Solo accesible por admins autenticados.
/// El state anti-CSRF se almacena en sesión y se verifica en el callback.
/// </summary>
[Authorize(Roles = "admin")]
[Route("mp")]
public class MercadoPagoOAuthController : Controller
{
    private const string AdminPage   = "/Admin/Configuracion/MercadoPago";
    private const string SessionKey  = "mp_oauth_state";

    private readonly IMercadoPagoOAuthService    _oauth;
    private readonly IConfiguration              _config;
    private readonly ILogger<MercadoPagoOAuthController> _logger;

    public MercadoPagoOAuthController(
        IMercadoPagoOAuthService         oauth,
        IConfiguration                   config,
        ILogger<MercadoPagoOAuthController> logger)
    {
        _oauth  = oauth;
        _config = config;
        _logger = logger;
    }

    // ── GET /mp/connect ───────────────────────────────────────────────────────
    /// <summary>
    /// Inicia el flujo OAuth: genera el state, lo guarda en sesión y redirige al
    /// formulario de autorización de MercadoPago.
    /// </summary>
    [HttpGet("connect")]
    public async Task<IActionResult> Connect()
    {
        var info = await _oauth.GetConnectionInfoAsync();

        if (!info.ClientIdConfigurado || !info.ClientSecretConfigurado)
        {
            TempData["Error"] =
                "Faltan las credenciales de la aplicación (MercadoPagoOAuth:ClientId / ClientSecret). " +
                "Configurálas en appsettings.json o como variables de entorno en Railway.";
            return Redirect(AdminPage);
        }

        // Generar state anti-CSRF y persistirlo en sesión
        var state = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString(SessionKey, state);

        var redirectUri = BuildRedirectUri();
        var oauthUrl    = await _oauth.BuildOAuthUrlAsync(state, redirectUri);

        _logger.LogInformation(
            "MP OAuth Connect: redirigiendo a MP — redirectUri={Uri}", redirectUri);

        return Redirect(oauthUrl);
    }

    // ── GET /mp/callback ──────────────────────────────────────────────────────
    /// <summary>
    /// Callback de MP tras la autorización del usuario.
    /// Valida el state, intercambia el code por tokens y persiste en BD.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery] string? error_description)
    {
        // ── 1. Error explícito de MP ────────────────────────────────────────
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning(
                "MP OAuth Callback: error de MP — error={Err} desc={Desc}", error, error_description);
            TempData["Error"] = $"MercadoPago rechazó la autorización: {error_description ?? error}";
            return Redirect(AdminPage);
        }

        // ── 2. Validar state anti-CSRF ──────────────────────────────────────
        var sessionState = HttpContext.Session.GetString(SessionKey);

        if (string.IsNullOrEmpty(sessionState))
        {
            _logger.LogWarning("MP OAuth Callback: sesión sin state — posible expiración o CSRF.");
            TempData["Error"] =
                "La sesión OAuth expiró o es inválida. Hacé clic en \"Conectar\" nuevamente.";
            return Redirect(AdminPage);
        }

        if (state != sessionState)
        {
            _logger.LogWarning(
                "MP OAuth Callback: state no coincide — session={Sess} received={Recv}",
                sessionState, state);
            TempData["Error"] =
                "Error de seguridad: el estado de la solicitud no coincide. Iniciá el proceso nuevamente.";
            return Redirect(AdminPage);
        }

        // Limpiar el state de sesión (one-time use)
        HttpContext.Session.Remove(SessionKey);

        // ── 3. Validar code ─────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(code))
        {
            _logger.LogWarning("MP OAuth Callback: no se recibió code en el callback.");
            TempData["Error"] =
                "No se recibió el código de autorización. Intentá nuevamente.";
            return Redirect(AdminPage);
        }

        // ── 4. Intercambiar code por tokens ─────────────────────────────────
        var redirectUri = BuildRedirectUri();

        _logger.LogInformation(
            "MP OAuth Callback: intercambiando code — redirectUri={Uri}", redirectUri);

        var ok = await _oauth.ExchangeCodeAsync(code, redirectUri);

        if (!ok)
        {
            TempData["Error"] =
                "No se pudo autorizar MercadoPago. " +
                "Verificá las credenciales de la aplicación y volvé a intentarlo.";
            return Redirect(AdminPage);
        }

        // ── 5. Éxito ─────────────────────────────────────────────────────────
        var info = await _oauth.GetConnectionInfoAsync();
        TempData["Exito"] =
            $"✅ Mercado Pago conectado correctamente. " +
            (info.UserId is not null ? $"Usuario: {info.UserId}" : string.Empty);

        return Redirect(AdminPage);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Construye el redirect_uri que MP usará en el callback.
    /// Prioridad: MercadoPagoOAuth:RedirectUri (config) → dinámico desde Request.
    /// El URI debe estar registrado exactamente igual en la app de MP Developer.
    /// </summary>
    private string BuildRedirectUri()
    {
        var fromConfig = _config["MercadoPagoOAuth:RedirectUri"];
        if (!string.IsNullOrWhiteSpace(fromConfig))
            return fromConfig.TrimEnd('/');

        // Construir dinámicamente — ForwardedHeaders ya ajustó Scheme a https en Railway
        return $"{Request.Scheme}://{Request.Host}/mp/callback";
    }
}
