namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Maestro de colores de producto. Tabla <c>variantesColores</c>.
/// No reutiliza la tabla <c>colores</c> del sistema de escritorio (colores de pantalla / pedidos viejos).
/// </summary>
public class VarianteColor
{
    public int     Id        { get; set; }
    public string  Nombre    { get; set; } = string.Empty;
    /// <summary>Color en hexadecimal (#RRGGBB) solo para mostrar una muestra; opcional.</summary>
    public string? CodigoHex { get; set; }
    public int     Orden     { get; set; }
    public bool    Activa    { get; set; } = true;
}
