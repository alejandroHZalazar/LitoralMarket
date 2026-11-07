namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Imagen del carrusel de promociones de la página de inicio (tabla promocionesCarrusel).
/// El blob nunca se carga en listados: se sirve bajo demanda por /images/promociones/{id}.
/// </summary>
public class PromocionCarrusel
{
    public int Id { get; set; }
    public byte[] Imagen { get; set; } = System.Array.Empty<byte>();
    public string? ContentType { get; set; }
    /// <summary>Texto alternativo (accesibilidad). Opcional.</summary>
    public string? Titulo { get; set; }
    public int Orden { get; set; }
    public System.DateTime FechaAlta { get; set; }
    /// <summary>Cambia al reemplazar la imagen: se usa como versión en la URL para el caché.</summary>
    public System.DateTime FechaModificacion { get; set; }
}
