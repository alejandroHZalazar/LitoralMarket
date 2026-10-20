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

    /// <summary>
    /// Cambia la contraseña de un cliente (Clientes.passwordHash). Verifica la actual con
    /// el mismo hasher del login y guarda la nueva con ese mismo mecanismo.
    /// </summary>
    Task<LitoralMarket.Application.DTOs.ResultadoCambioPassword> CambiarPasswordClienteAsync(
        int clienteId, string actual, string nueva);

    /// <summary>
    /// Cambia la contraseña de un administrador (usuarios.password, texto plano). Verifica la
    /// actual con la misma lógica de <see cref="ValidarAdminAsync"/>; si el usuario está migrado
    /// (password_migrated = 1) también recalcula password_hash, que es lo que valida el login.
    /// </summary>
    Task<LitoralMarket.Application.DTOs.ResultadoCambioPassword> CambiarPasswordAdminAsync(
        int usuarioId, string actual, string nueva);
}
