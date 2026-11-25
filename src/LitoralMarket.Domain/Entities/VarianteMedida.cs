namespace LitoralMarket.Domain.Entities;

/// <summary>Maestro de medidas (S, M, L, 38, 500 ml...). Tabla <c>variantesMedidas</c>.</summary>
public class VarianteMedida
{
    public int    Id     { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int    Orden  { get; set; }
    public bool   Activa { get; set; } = true;
}
