namespace LitoralMarket.Application.DTOs;

public class ProductoDto
{
    public int Id { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string? DescripcionLarga { get; set; }
    /// <summary>true si el producto tiene imagen almacenada en BD (columna imagen IS NOT NULL).</summary>
    public bool TieneImagen { get; set; }
    /// <summary>URL relativa a la imagen servida bajo demanda. Nunca carga el blob en listings.</summary>
    public string? Imagen => TieneImagen ? $"/images/productos/{Id}" : null;
    public int? RubroId { get; set; }
    public string? RubroNombre { get; set; }
    public decimal Precio { get; set; }
    public decimal Stock { get; set; }
    public bool TieneStock => Stock > 0;
    public bool EsPromocion { get; set; }
    public bool Fraccionado { get; set; }
    public string? CodBarras { get; set; }
    public string? CodProveedor { get; set; }
    public int? Iva { get; set; }
}
