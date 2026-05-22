namespace LitoralMarket.Domain.Entities;

public class PrecioProducto
{
    public int Id { get; set; }
    public int? FkProducto { get; set; }
    public decimal? Precio { get; set; }

    public Producto? Producto { get; set; }
}
