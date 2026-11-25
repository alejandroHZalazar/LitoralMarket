namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Hora local de Argentina. La vigencia de las promociones se guarda y se compara en hora argentina (igual que en Comercial Web);
/// el servidor puede correr en UTC.
/// </summary>
public static class HoraArgentina
{
    private static readonly TimeZoneInfo? Zona = Buscar();

    public static DateTime Ahora()
        => Zona != null
            ? TimeZoneInfo.ConvertTime(DateTime.UtcNow, Zona)
            : DateTime.UtcNow.AddHours(-3);

    private static TimeZoneInfo? Buscar()
    {
        foreach (var id in new[] { "America/Argentina/Buenos_Aires", "Argentina Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* probar el siguiente id */ }
        }
        return null;   // sin datos de zona (contenedor sin tzdata): UTC-3 fijo
    }
}
