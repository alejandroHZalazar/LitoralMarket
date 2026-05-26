namespace LitoralMarket.Application.Helpers;

/// <summary>
/// Utilidades para normalizar números de teléfono argentinos.
/// </summary>
public static class PhoneHelper
{
    /// <summary>
    /// Normaliza un número de teléfono argentino al formato requerido por WhatsApp:
    /// <c>549XXXXXXXXXX</c> (código de país 54 + prefijo móvil 9 + 10 dígitos).
    /// </summary>
    /// <remarks>
    /// Formatos de entrada soportados (todos producen el mismo resultado):
    /// <list type="bullet">
    ///   <item><c>+54 9 362 4433110</c>  → ya en formato internacional</item>
    ///   <item><c>+54 362 4433110</c>    → con código de país, sin el 9 móvil</item>
    ///   <item><c>0362 4433110</c>       → marcación local con 0 inicial</item>
    ///   <item><c>3624433110</c>         → 10 dígitos sin prefijo (área + número)</item>
    ///   <item><c>0362 15 443311</c>     → con prefijo móvil viejo "15"</item>
    /// </list>
    /// Si el número tiene más de 10 dígitos luego de eliminar todos los prefijos
    /// conocidos, se toman los últimos 10 dígitos.
    /// </remarks>
    /// <returns>
    /// Cadena de dígitos apta para <c>https://wa.me/{result}</c>,
    /// o <see cref="string.Empty"/> si el input es nulo, vacío o sin dígitos.
    /// </returns>
    public static string NormalizarParaWhatsApp(string? telefono)
    {
        if (string.IsNullOrWhiteSpace(telefono))
            return string.Empty;

        // 1. Extraer solo dígitos
        var d = new string(telefono.Where(char.IsDigit).ToArray());
        if (d.Length == 0)
            return string.Empty;

        // 2. Quitar código de país argentino (54) si ya viene incluido
        //    Caso "549..." → quitar "549", quedarse con los dígitos locales
        //    Caso "54..."  → quitar "54",  sin el "9" (se agrega al final)
        if (d.StartsWith("549") && d.Length > 10)
            d = d[3..];
        else if (d.StartsWith("54") && d.Length > 10)
            d = d[2..];

        // 3. Quitar el 0 inicial de marcación local (ej: "0362...")
        if (d.StartsWith("0"))
            d = d[1..];

        // 4. Quitar el prefijo móvil viejo "15" si aparece inmediatamente
        //    después del código de área (2, 3 o 4 dígitos).
        //    Se prueba de mayor a menor longitud de área para evitar falsos positivos.
        if (d.Length > 10)
        {
            foreach (var areaLen in new[] { 4, 3, 2 })
            {
                if (d.Length > areaLen + 2 && d.Substring(areaLen, 2) == "15")
                {
                    d = d[..areaLen] + d[(areaLen + 2)..];
                    break;
                }
            }
        }

        // 5. Si quedan más de 10 dígitos por algún formato imprevisto,
        //    tomar los últimos 10 (los más significativos del número local).
        if (d.Length > 10)
            d = d[^10..];

        // 6. Armar el número final: país (54) + móvil (9) + 10 dígitos locales
        return "549" + d;
    }
}
