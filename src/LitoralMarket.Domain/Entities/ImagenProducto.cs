namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Imagen de un producto (tabla imagenesProductos). Un producto puede tener varias;
/// una marcada como principal. El blob nunca se carga en listados: solo se sirve
/// bajo demanda por URL (/images/productos/{id} para la principal,
/// /images/productos/img/{imagenId} para una puntual).
/// </summary>
public class ImagenProducto
{
    public int Id { get; set; }
    public int FkProducto { get; set; }
    public byte[] Imagen { get; set; } = System.Array.Empty<byte>();
    public string? ContentType { get; set; }
    public bool EsPrincipal { get; set; }
    public int Orden { get; set; }
    public bool Baja { get; set; }
    public System.DateTime FechaAlta { get; set; }

    public Producto? Producto { get; set; }
}
