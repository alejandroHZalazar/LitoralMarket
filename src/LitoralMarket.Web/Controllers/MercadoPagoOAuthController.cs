using System.Security.Cryptography;
using System.Text;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LitoralMarket.Web.Controllers;

/// <summary>
/// Maneja el flujo OAuth de MercadoPago:
///
///   GET /mp/connect   → genera state, guarda en cookie firmada y redirige a MP
///   GET /mp/callback  → recibe code, valida state desde cookie, intercambia tokens
///
/// /connect requiere admin. /callback es AllowAnonymous porque el browser viene
/// desde MP (cross-site) y la cookie de auth puede no enviarse — el state firmado
/// en la cookie HttpOnly anti-CSRF es la garantía de seguridad.
/// </summary>
[Route("mp")]
public class MercadoPagoOAuthController : Controller
{
    private const string AdminPage      = "/Admin/Configuracion/MercadoPago";
    private const string CookieState    = "mp_oauth_state";
    private const string CookieVerifier = "mp_oauth_verifier";

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
    [Authorize(Roles = "admin")]
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

        // Generar state anti-CSRF y PKCE (code_verifier + code_challenge).
        // Ambos viajan en cookies HttpOnly con SameSite=Lax para sobrevivir al
        // redirect cross-site desde MP.
        var state         = Guid.NewGuid().ToString("N");
        var codeVerifier  = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);

        var cookieOpts = new CookieOptions
        {
            HttpOnly    = true,
            Secure      = true,
            SameSite    = SameSiteMode.Lax,
            Expires     = DateTimeOffset.UtcNow.AddMinutes(15),
            IsEssential = true,
            Path        = "/"
        };
        Response.Cookies.Append(CookieState,    state,        cookieOpts);
        Response.Cookies.Append(CookieVerifier, codeVerifier, cookieOpts);

        var redirectUri = BuildRedirectUri();
        var oauthUrl    = await _oauth.BuildOAuthUrlAsync(state, redirectUri, codeChallenge);

        _logger.LogInformation(
            "MP OAuth Connect: redirigiendo a MP — redirectUri={Uri} state={State}",
            redirectUri, state);

        return Redirect(oauthUrl);
    }

    // ── GET /mp/callback ──────────────────────────────────────────────────────
    /// <summary>
    /// Callback de MP tras la autorización del usuario.
    /// Valida el state, intercambia el code por tokens y persiste en BD.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery] string? error_description)
    {
        _logger.LogInformation(
            "MP OAuth Callback: recibido — codePresent={HasCode} state={State} error={Err}",
            !string.IsNullOrEmpty(code), state, error);

        // ── 1. Error explícito de MP ────────────────────────────────────────
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning(
                "MP OAuth Callback: error de MP — error={Err} desc={Desc}", error, error_description);
            TempData["Error"] = $"MercadoPago rechazó la autorización: {error_description ?? error}";
            return Redirect(AdminPage);
        }

        // ── 2. Validar state anti-CSRF (desde cookie firmada) ───────────────
        Request.Cookies.TryGetValue(CookieState,    out var cookieState);
        Request.Cookies.TryGetValue(CookieVerifier, out var cookieVerifier);

        if (string.IsNullOrEmpty(cookieState) || string.IsNullOrEmpty(cookieVerifier))
        {
            _logger.LogWarning("MP OAuth Callback: cookies de state/verifier ausentes — posible expiración o CSRF.");
            TempData["Error"] =
                "La sesión OAuth expiró o es inválida. Hacé clic en \"Conectar\" nuevamente.";
            return Redirect(AdminPage);
        }

        if (state != cookieState)
        {
            _logger.LogWarning(
                "MP OAuth Callback: state no coincide — cookie={Cookie} received={Recv}",
                cookieState, state);
            TempData["Error"] =
                "Error de seguridad: el estado de la solicitud no coincide. Iniciá el proceso nuevamente.";
            return Redirect(AdminPage);
        }

        // Limpiar las cookies (one-time use)
        Response.Cookies.Delete(CookieState);
        Response.Cookies.Delete(CookieVerifier);

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

        var ok = await _oauth.ExchangeCodeAsync(code, redirectUri, cookieVerifier);

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

    // ── PKCE helpers (RFC 7636) ───────────────────────────────────────────────

    /// <summary>
    /// Genera un code_verifier aleatorio (43-128 chars URL-safe) — RFC 7636 §4.1.
    /// </summary>
    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32]; // → 43 chars base64url
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Calcula code_challenge = BASE64URL(SHA256(code_verifier)) — RFC 7636 §4.2.
    /// </summary>
    private static string ComputeCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    /// <summary>Base64 URL-safe sin padding (RFC 4648 §5).</summary>
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
               .TrimEnd('=')
               .Replace('+', '-')
               .Replace('/', '_');
}
