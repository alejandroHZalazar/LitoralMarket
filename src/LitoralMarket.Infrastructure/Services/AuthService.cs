using System.Security.Cryptography;
using System.Text;
using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext          _db;
    private readonly IParametrosService    _params;
    private readonly PasswordHasher<string> _hasher;

    public AuthService(AppDbContext db, IParametrosService params_)
    {
        _db     = db;
        _params = params_;
        _hasher = new PasswordHasher<string>();
    }

    // ──────────────────────────────────────────────────────────────
    // Clientes (modo credenciales)
    // ──────────────────────────────────────────────────────────────
    public async Task<Cliente?> ValidarCredencialesAsync(string email, string password)
    {
        var cliente = await _db.Clientes
            .FirstOrDefaultAsync(c => c.Email == email && c.Baja != true);

        if (cliente is null || cliente.PasswordHash is null)
            return null;

        var result = _hasher.VerifyHashedPassword(email, cliente.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : cliente;
    }

    // ──────────────────────────────────────────────────────────────
    // Administradores (tabla usuarios)
    // ──────────────────────────────────────────────────────────────
    public async Task<Usuario?> ValidarAdminAsync(string nombre, string password)
    {
        // Obtener el tipo que identifica al administrador
        var adminTipoStr = await _params.GetValorAsync("configuracion", "admin");
        if (!int.TryParse(adminTipoStr, out var adminTipo))
            return null;

        var usuario = await _db.Usuarios
            .FirstOrDefaultAsync(u =>
                u.Nombre == nombre &&
                u.Tipo   == adminTipo &&
                u.Baja   != true);

        if (usuario is null) return null;

        return VerificarPasswordAdmin(usuario, password) ? usuario : null;
    }

    // Misma verificación que usa el login de administradores (ValidarAdminAsync).
    private static bool VerificarPasswordAdmin(Usuario usuario, string password)
    {
        if (usuario.PasswordMigrated == true && !string.IsNullOrEmpty(usuario.PasswordHash))
        {
            // Contraseña migrada: SHA-256 en Base64
            return Sha256Base64(password) == usuario.PasswordHash;
        }

        // Contraseña en texto plano (sistema legado)
        return usuario.Password == password;
    }

    // ──────────────────────────────────────────────────────────────
    // Cambio de contraseña (usuario autenticado)
    // ──────────────────────────────────────────────────────────────
    public async Task<ResultadoCambioPassword> CambiarPasswordClienteAsync(int clienteId, string actual, string nueva)
    {
        var cliente = await _db.Clientes
            .FirstOrDefaultAsync(c => c.Id == clienteId && c.Baja != true);

        if (cliente is null || cliente.PasswordHash is null)
            return ResultadoCambioPassword.UsuarioNoEncontrado;

        // Mismo hasher y misma llamada que ValidarCredencialesAsync.
        var verificacion = _hasher.VerifyHashedPassword(cliente.Email ?? string.Empty, cliente.PasswordHash, actual);
        if (verificacion == PasswordVerificationResult.Failed)
            return ResultadoCambioPassword.ActualIncorrecta;

        cliente.PasswordHash = HashPassword(nueva);
        await _db.SaveChangesAsync();
        return ResultadoCambioPassword.Ok;
    }

    public async Task<ResultadoCambioPassword> CambiarPasswordAdminAsync(int usuarioId, string actual, string nueva)
    {
        // Mismos filtros que el login de administradores.
        var adminTipoStr = await _params.GetValorAsync("configuracion", "admin");
        if (!int.TryParse(adminTipoStr, out var adminTipo))
            return ResultadoCambioPassword.UsuarioNoEncontrado;

        var usuario = await _db.Usuarios
            .FirstOrDefaultAsync(u => u.Id == usuarioId && u.Tipo == adminTipo && u.Baja != true);

        if (usuario is null)
            return ResultadoCambioPassword.UsuarioNoEncontrado;

        if (!VerificarPasswordAdmin(usuario, actual))
            return ResultadoCambioPassword.ActualIncorrecta;

        // Texto plano, según la estructura actual de la tabla usuarios.
        usuario.Password = nueva;

        // Si está migrado, el login valida contra password_hash (no contra password):
        // se recalcula con el mismo SHA-256 para que la nueva contraseña sea la vigente.
        if (usuario.PasswordMigrated == true)
            usuario.PasswordHash = Sha256Base64(nueva);

        await _db.SaveChangesAsync();
        return ResultadoCambioPassword.Ok;
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers de hash
    // ──────────────────────────────────────────────────────────────
    public string HashPassword(string password) =>
        _hasher.HashPassword(string.Empty, password);

    public bool VerifyPassword(string hashedPassword, string providedPassword)
    {
        var result = _hasher.VerifyHashedPassword(string.Empty, hashedPassword, providedPassword);
        return result != PasswordVerificationResult.Failed;
    }

    private static string Sha256Base64(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }
}
