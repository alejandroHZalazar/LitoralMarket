namespace LitoralMarket.Domain.Entities;

public class CostoProducto
{
    public int      Id         { get; set; }
    public int?     FkProducto { get; set; }
    public decimal? Costo      { get; set; }

    // Navegación
    public Producto? Producto { get; set; }
}
