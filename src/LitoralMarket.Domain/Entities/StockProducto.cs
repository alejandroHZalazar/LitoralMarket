namespace LitoralMarket.Domain.Entities;

public class StockProducto
{
    public int Id { get; set; }
    public int? FkProducto { get; set; }
    public decimal? Cantidad { get; set; }
    public decimal? CantidadMinima { get; set; }

    public Producto? Producto { get; set; }
}
