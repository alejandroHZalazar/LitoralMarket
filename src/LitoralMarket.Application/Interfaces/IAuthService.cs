using LitoralMarket.Domain.Entities;

namespace LitoralMarket.Application.Interfaces;

public interface IAuthService
{
    /// <summary>Valida credenciales de un cliente (tabla Clientes).</summary>
    Task<Cliente?> ValidarCredencialesAsync(string email, string password);

    /// <summary>
    /// Valida credenciales de un usuario administrador (tabla usuarios).
    /// Solo acepta usuarios cuyo tipo coincide con el parámetro configuracion/admin
    /// y que no están dados de baja.
    /// Soporta contraseña en texto plano (password_migrated IS NULL/0)
    /// y SHA-256 Base64 (password_migrated = 1).
    /// </summary>
    Task<Usuario?> ValidarAdminAsync(string nombre, string password);

    string HashPassword(string password);
    bool VerifyPassword(string hashedPassword, string providedPassword);
}
