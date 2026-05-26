namespace LitoralMarket.Application.DTOs;

/// <summary>Estado de la conexión OAuth de MercadoPago para mostrar en la UI.</summary>
public record MpConnectionInfoDto(
    bool    Conectado,
    string? UserId,
    string? PublicKeyMascara,
    string? ConnectedAt,
    string? ExpiresAt,
    bool    ClientIdConfigurado,
    bool    ClientSecretConfigurado
);
