namespace LitoralMarket.Application.Helpers;

/// <summary>
/// Reglas de la nueva contraseña al cambiarla: al menos una letra, un número y un
/// símbolo, y que la confirmación coincida. Devuelve todos los errores juntos.
/// </summary>
public static class PasswordPolicyHelper
{
    public static List<string> Validar(string? nueva, string? confirmacion)
    {
        var errores = new List<string>();

        if (string.IsNullOrEmpty(nueva))
        {
            errores.Add("Ingresá la nueva contraseña.");
            return errores;
        }

        if (!nueva.Any(char.IsLetter))
            errores.Add("La nueva contraseña debe contener al menos una letra.");
        if (!nueva.Any(char.IsDigit))
            errores.Add("La nueva contraseña debe contener al menos un número.");
        if (!nueva.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)))
            errores.Add("La nueva contraseña debe contener al menos un símbolo (por ejemplo: ! @ # $ % . -).");

        if (nueva != confirmacion)
            errores.Add("La confirmación no coincide con la nueva contraseña.");

        return errores;
    }
}
