namespace LitoralMarket.Application.DTOs;

/// <summary>Producto de la selección manual del Inicio, con su estado de visibilidad actual.</summary>
public class ProductoInicioItemDto
{
    public int     ProductoId   { get; set; }
    public string? CodProveedor { get; set; }
    public string? CodBarras    { get; set; }
    public string  Descripcion  { get; set; } = string.Empty;
    public int     Orden        { get; set; }
    public decimal Stock        { get; set; }
    public bool    Baja         { get; set; }
    /// <summary>True si hoy se muestra en el Inicio (no está de baja y cumple el criterio de stock).</summary>
    public bool    Visible      { get; set; }
    /// <summary>Por qué no se muestra ("Dado de baja", "Sin stock"). Null si es visible.</summary>
    public string? Motivo       { get; set; }
}

public class GuardarSeleccionInicioResultado
{
    public bool    Ok    { get; set; }
    public string? Error { get; set; }
    public static GuardarSeleccionInicioResultado Correcto() => new() { Ok = true };
    public static GuardarSeleccionInicioResultado Fallo(string error) => new() { Ok = false, Error = error };
}
