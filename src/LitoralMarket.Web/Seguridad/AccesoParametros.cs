using Microsoft.AspNetCore.Identity;

namespace LitoralMarket.Web.Seguridad;

/// <summary>
/// Segunda clave para /Admin/Parametros (además de ser administrador). Acá solo se guarda su hash PBKDF2; se puede
/// reemplazar sin recompilar con la configuración "Seguridad:ClaveParametrosHash" (mismo formato).
/// El desbloqueo queda en la sesión, atado al usuario, y vence tras <see cref="Inactividad"/> sin usar la pantalla
/// o al cerrar sesión.
/// </summary>
public static class AccesoParametros
{
    private const string HashPorDefecto =
        "AQAAAAIAAYagAAAAEAxLB4MHFqMqdenbUN/KFR8C7Vrovms+s+xNUBqZ97RRAP8ZRe7wUT9/u4OASVUrPQ==";
    private const string ClaveSesion = "parametros_desbloqueo";

    public static readonly TimeSpan Inactividad = TimeSpan.FromMinutes(15);

    public static bool Verificar(string? clave, IConfiguration config)
    {
        if (string.IsNullOrEmpty(clave)) return false;
        var hash = config["Seguridad:ClaveParametrosHash"];
        if (string.IsNullOrWhiteSpace(hash)) hash = HashPorDefecto;
        try
        {
            return new PasswordHasher<object>().VerifyHashedPassword(new object(), hash, clave)
                   != PasswordVerificationResult.Failed;
        }
        catch (FormatException) { return false; }   // hash mal copiado en la configuración: no deja pasar
    }

    public static void Desbloquear(HttpContext ctx) =>
        ctx.Session.SetString(ClaveSesion, $"{DateTime.UtcNow.Ticks}|{ctx.User.Identity?.Name}");

    public static void Bloquear(HttpContext ctx) => ctx.Session.Remove(ClaveSesion);

    /// <summary>true si sigue desbloqueado para este usuario; en ese caso renueva el plazo.</summary>
    public static bool EstaDesbloqueado(HttpContext ctx)
    {
        var partes = ctx.Session.GetString(ClaveSesion)?.Split('|', 2);
        if (partes is not { Length: 2 } || !long.TryParse(partes[0], out var ticks)) return false;

        if (partes[1] != (ctx.User.Identity?.Name ?? string.Empty)
            || DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) > Inactividad)
        {
            Bloquear(ctx);
            return false;
        }
        Desbloquear(ctx);
        return true;
    }
}
