namespace LitoralMarket.Application.DTOs;

/// <summary>Promoción del carrusel sin el blob de la imagen.</summary>
public class PromocionCarruselDto
{
    public int     Id      { get; set; }
    public string? Titulo  { get; set; }
    public int     Orden   { get; set; }
    /// <summary>Marca de versión de la imagen (para la URL y el caché del navegador).</summary>
    public long    Version { get; set; }
}

/// <summary>Imagen lista para servir.</summary>
public record PromocionImagen(byte[] Bytes, string ContentType);

/// <summary>Resultado de una operación de administración del carrusel.</summary>
public record ResultadoPromocion(bool Ok, string? Error = null, int? Id = null)
{
    public static ResultadoPromocion Correcto(int? id = null) => new(true, null, id);
    public static ResultadoPromocion Fallo(string error) => new(false, error);
}
