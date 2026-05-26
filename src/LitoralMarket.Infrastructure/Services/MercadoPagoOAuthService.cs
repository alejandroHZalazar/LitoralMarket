using System.Text;
using System.Text.Json;
using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Implementa el flujo OAuth de MercadoPago (authorization_code y refresh_token).
///
/// Credenciales de la APP (client_id / client_secret) se leen de configuración
/// (appsettings.json o variables de entorno), NUNCA de la BD ni del código.
///
/// Los tokens obtenidos se persisten en la tabla <c>parametros</c>:
///   mercadopago / accessToken   → actualiza fila existente (compat. sistema actual)
///   mercadopago / publicKey     → actualiza fila existente (compat. sistema actual)
///   mercadopago / refreshToken  → crea si no existe
///   mercadopago / userId        → crea si no existe
///   mercadopago / connectedAt   → crea si no existe
///   mercadopago / expiresAt     → crea si no existe
/// </summary>
public class MercadoPagoOAuthService : IMercadoPagoOAuthService
{
    private const string Modulo       = "mercadopago";
    private const string UrlToken     = "https://api.mercadopago.com/oauth/token";
    private const string UrlAuth      = "https://auth.mercadopago.com/authorization";
    private const int    DiasRefresh  = 7;   // refresh automático si expira en < 7 días

    private readonly AppDbContext                    _db;
    private readonly IMemoryCache                    _cache;
    private readonly IHttpClientFactory              _http;
    private readonly IConfiguration                  _config;
    private readonly ILogger<MercadoPagoOAuthService> _logger;

    // ── Credenciales de la app (desde config/env vars, NUNCA hardcodeadas) ────
    private string ClientId     => _config["MercadoPagoOAuth:ClientId"]     ?? string.Empty;
    private string ClientSecret => _config["MercadoPagoOAuth:ClientSecret"] ?? string.Empty;

    public MercadoPagoOAuthService(
        AppDbContext                    db,
        IMemoryCache                    cache,
        IHttpClientFactory              http,
        IConfiguration                  config,
        ILogger<MercadoPagoOAuthService> logger)
    {
        _db     = db;
        _cache  = cache;
        _http   = http;
        _config = config;
        _logger = logger;
    }

    // ── Build OAuth URL (con PKCE) ────────────────────────────────────────────
    public Task<string> BuildOAuthUrlAsync(string state, string redirectUri, string codeChallenge)
    {
        var url = UrlAuth
            + $"?client_id={Uri.EscapeDataString(ClientId)}"
            + "&response_type=code"
            + "&platform_id=mp"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
            + "&code_challenge_method=S256";

        return Task.FromResult(url);
    }

    // ── Intercambio code → tokens (con PKCE) ──────────────────────────────────
    public async Task<bool> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier)
    {
        _logger.LogInformation("MP OAuth: exchange authorization_code para redirectUri={Uri}", redirectUri);

        return await PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"]     = ClientId,
            ["client_secret"] = ClientSecret,
            ["grant_type"]    = "authorization_code",
            ["code"]          = code,
            ["redirect_uri"]  = redirectUri,
            ["code_verifier"] = codeVerifier
        });
    }

    // ── Refresh token ─────────────────────────────────────────────────────────
    public async Task<bool> RefreshTokenAsync()
    {
        var refreshToken = await GetParamDirectAsync("refreshToken");
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            _logger.LogWarning("MP OAuth: RefreshToken — no hay refresh_token almacenado.");
            return false;
        }

        _logger.LogInformation("MP OAuth: intentando refresh_token.");

        return await PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"]     = ClientId,
            ["client_secret"] = ClientSecret,
            ["grant_type"]    = "refresh_token",
            ["refresh_token"] = refreshToken
        });
    }

    // ── Obtener access token activo (con auto-refresh si está por expirar) ────
    public async Task<string?> GetAccessTokenAsync()
    {
        var expiresAtStr = await GetParamDirectAsync("expiresAt");
        if (DateTime.TryParse(expiresAtStr, out var expiresAt))
        {
            if (expiresAt - DateTime.UtcNow < TimeSpan.FromDays(DiasRefresh))
            {
                _logger.LogInformation(
                    "MP OAuth: token expira {At:s}, intentando refresh automático.", expiresAt);
                await RefreshTokenAsync();
            }
        }

        return await GetParamDirectAsync("accessToken");
    }

    // ── Desconectar ───────────────────────────────────────────────────────────
    public async Task<bool> DisconnectAsync()
    {
        try
        {
            var claves = new[]
            {
                "accessToken", "publicKey", "refreshToken",
                "userId", "connectedAt", "expiresAt"
            };

            var rows = await _db.Parametros
                .Where(p => p.Modulo == Modulo && claves.Contains(p.ParametroNombre))
                .ToListAsync();

            foreach (var row in rows)
            {
                row.Valor = null;
                InvalidarCache(row.ParametroNombre!);
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("MP OAuth: cuenta desconectada.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MP OAuth: error al desconectar.");
            return false;
        }
    }

    // ── Info de conexión para la UI ───────────────────────────────────────────
    public async Task<MpConnectionInfoDto> GetConnectionInfoAsync()
    {
        // Carga todos los parámetros MP en una sola consulta
        var rows = await _db.Parametros
            .Where(p => p.Modulo == Modulo)
            .Select(p => new { p.ParametroNombre, p.Valor })
            .ToListAsync();

        var get = (string nombre) =>
            rows.FirstOrDefault(r => r.ParametroNombre == nombre)?.Valor;

        var accessToken = get("accessToken");
        var userId      = get("userId");
        var publicKey   = get("publicKey");

        var conectado = !string.IsNullOrWhiteSpace(accessToken)
                     && !string.IsNullOrWhiteSpace(userId);

        // Public Key: mostrar solo sufijo (nunca completo)
        string? publicKeyMascara = null;
        if (!string.IsNullOrWhiteSpace(publicKey))
        {
            publicKeyMascara = publicKey.Length > 12
                ? $"APP_USR-•••••••••{publicKey[^4..]}"
                : "APP_USR-••••";
        }

        // Formatear fechas para la UI
        string? connectedAtDisplay = FormatarFecha(get("connectedAt"));
        string? expiresAtDisplay   = FormatarFecha(get("expiresAt"));

        return new MpConnectionInfoDto(
            Conectado:               conectado,
            UserId:                  userId,
            PublicKeyMascara:        publicKeyMascara,
            ConnectedAt:             connectedAtDisplay,
            ExpiresAt:               expiresAtDisplay,
            ClientIdConfigurado:     !string.IsNullOrWhiteSpace(ClientId),
            ClientSecretConfigurado: !string.IsNullOrWhiteSpace(ClientSecret)
        );
    }

    // ── POST al endpoint de tokens MP ─────────────────────────────────────────
    private async Task<bool> PostTokenAsync(Dictionary<string, string> payload)
    {
        try
        {
            var http    = _http.CreateClient("MercadoPago");
            var content = new FormUrlEncodedContent(payload);

            var req = new HttpRequestMessage(HttpMethod.Post, UrlToken) { Content = content };
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var resp = await http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                // El body de error de MP no trae secretos — viene tipo {"error":"invalid_grant", ...}
                _logger.LogError(
                    "MP OAuth: error HTTP {Code} en POST oauth/token — body={Body}",
                    (int)resp.StatusCode, body);
                return false;
            }

            using var doc = JsonDocument.Parse(body);
            var root      = doc.RootElement;

            var accessToken  = GetStr(root, "access_token");
            var refreshToken = GetStr(root, "refresh_token");
            var publicKey    = GetStr(root, "public_key");
            var userId       = root.TryGetProperty("user_id", out var uid) ? uid.ToString() : null;
            var expiresIn    = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt64(out var secs)
                               ? secs : (long?)null;

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                _logger.LogError("MP OAuth: la respuesta no contiene access_token.");
                return false;
            }

            var now       = DateTime.UtcNow;
            var expiresAt = expiresIn.HasValue
                ? now.AddSeconds(expiresIn.Value)
                : now.AddDays(180);

            // Persistir tokens — upsert por (modulo, parametroNombre)
            await UpsertAsync("accessToken",  accessToken);
            await UpsertAsync("publicKey",    publicKey    ?? string.Empty);
            await UpsertAsync("refreshToken", refreshToken ?? string.Empty);
            await UpsertAsync("userId",       userId       ?? string.Empty);
            await UpsertAsync("connectedAt",  now.ToString("o"));
            await UpsertAsync("expiresAt",    expiresAt.ToString("o"));

            _logger.LogInformation(
                "MP OAuth: tokens guardados — userId={UserId} expiresAt={Exp:s}",
                userId, expiresAt);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MP OAuth: excepción inesperada en PostTokenAsync.");
            return false;
        }
    }

    // ── Upsert parametro ──────────────────────────────────────────────────────
    private async Task UpsertAsync(string nombre, string valor)
    {
        var existing = await _db.Parametros
            .FirstOrDefaultAsync(p => p.Modulo == Modulo && p.ParametroNombre == nombre);

        if (existing is null)
            _db.Parametros.Add(new Parametro
            {
                Modulo          = Modulo,
                ParametroNombre = nombre,
                Valor           = valor
            });
        else
            existing.Valor = valor;

        await _db.SaveChangesAsync();
        InvalidarCache(nombre);
    }

    // Lee directamente de BD (sin cache) para obtener valor fresco
    private async Task<string?> GetParamDirectAsync(string nombre) =>
        await _db.Parametros
            .Where(p => p.Modulo == Modulo && p.ParametroNombre == nombre)
            .Select(p => p.Valor)
            .FirstOrDefaultAsync();

    private void InvalidarCache(string nombre)
    {
        _cache.Remove($"param_{Modulo}_{nombre}");
        _logger.LogDebug("MP OAuth: cache invalidado para {M}/{N}", Modulo, nombre);
    }

    private static string? GetStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var p) ? p.GetString() : null;

    private static string? FormatarFecha(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        return DateTime.TryParse(iso, null,
                   System.Globalization.DateTimeStyles.RoundtripKind,
                   out var dt)
            ? dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            : iso;
    }
}
