namespace LitoralMarket.Application.DTOs;

/// <summary>
/// Metadatos de una imagen de producto (sin el blob). El blob se sirve por URL.
/// </summary>
public class ProductoImagenDto
{
    public int Id { get; set; }
    public bool EsPrincipal { get; set; }
    public int Orden { get; set; }

    /// <summary>URL para servir esta imagen puntual bajo demanda.</summary>
    public string Url => $"/images/productos/img/{Id}";
}
