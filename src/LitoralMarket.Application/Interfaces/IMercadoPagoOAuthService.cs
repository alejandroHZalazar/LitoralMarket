using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

/// <summary>
/// Gestión del flujo OAuth de MercadoPago (authorization_code).
/// Los tokens se persisten en la tabla <c>parametros</c> (modulo='mercadopago')
/// y son consumidos automáticamente por el sistema de pagos existente.
/// </summary>
public interface IMercadoPagoOAuthService
{
    /// <summary>Construye la URL de autorización de MP con el state anti-CSRF.</summary>
    Task<string> BuildOAuthUrlAsync(string state, string redirectUri);

    /// <summary>
    /// Intercambia el <paramref name="code"/> recibido por tokens y los persiste en BD.
    /// Actualiza accessToken, refreshToken, publicKey, userId, connectedAt, expiresAt.
    /// </summary>
    Task<bool> ExchangeCodeAsync(string code, string redirectUri);

    /// <summary>
    /// Devuelve el access token activo.
    /// Si está próximo a expirar (&lt; 7 días) intenta un refresh automático.
    /// </summary>
    Task<string?> GetAccessTokenAsync();

    /// <summary>Renueva el access token usando el refresh token almacenado.</summary>
    Task<bool> RefreshTokenAsync();

    /// <summary>Borra todos los datos OAuth de BD (no invalida el token en MP).</summary>
    Task<bool> DisconnectAsync();

    /// <summary>Devuelve el estado de conexión actual para mostrar en la UI.</summary>
    Task<MpConnectionInfoDto> GetConnectionInfoAsync();
}
