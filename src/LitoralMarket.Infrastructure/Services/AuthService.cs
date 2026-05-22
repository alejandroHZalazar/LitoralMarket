using System.Security.Cryptography;
using System.Text;
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

        // Verificar contraseña
        if (usuario.PasswordMigrated == true && !string.IsNullOrEmpty(usuario.PasswordHash))
        {
            // Contraseña migrada: SHA-256 en Base64
            var hashCalculado = Sha256Base64(password);
            return hashCalculado == usuario.PasswordHash ? usuario : null;
        }

        // Contraseña en texto plano (sistema legado)
        return usuario.Password == password ? usuario : null;
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
