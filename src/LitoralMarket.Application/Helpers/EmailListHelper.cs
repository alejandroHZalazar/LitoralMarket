using System.Net.Mail;

namespace LitoralMarket.Application.Helpers;

/// <summary>
/// Parseo de parámetros de configuración que admiten uno o varios emails
/// separados por ';' (ej. mail/emailAdmin). Un valor con un solo email —
/// el formato usado hasta ahora— sigue funcionando igual: produce una lista
/// de un elemento.
/// </summary>
public static class EmailListHelper
{
    /// <summary>
    /// Separa por ';', recorta espacios, descarta vacíos y direcciones con
    /// formato inválido, y quita duplicados (sin distinguir mayúsculas/minúsculas).
    /// Nunca lanza excepción: una dirección inválida simplemente se excluye.
    /// </summary>
    public static List<string> Parse(string? valor)
    {
        var resultado = new List<string>();
        if (string.IsNullOrWhiteSpace(valor)) return resultado;

        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var crudo in valor.Split(';'))
        {
            var email = crudo.Trim();
            if (email.Length == 0) continue;
            if (!EsValido(email)) continue;
            if (vistos.Add(email)) resultado.Add(email);
        }

        return resultado;
    }

    private static bool EsValido(string email)
    {
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
