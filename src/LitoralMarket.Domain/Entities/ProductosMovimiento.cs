namespace LitoralMarket.Domain.Entities;

public class ProductosMovimiento
{
    public long     Id              { get; set; }
    public int?     FkProducto      { get; set; }

    /// <summary>3 = egreso por venta ecommerce</summary>
    public int?     TipoMovimiento  { get; set; }

    public string?  Descripcion     { get; set; }
    public decimal? StockAnt        { get; set; }
    public decimal? StockAct        { get; set; }
    public decimal? Costo           { get; set; }
    public decimal? Venta           { get; set; }
    public decimal? Cantidad        { get; set; }
    public decimal? PrecioProveedor { get; set; }
    public DateTime? FechaEntrega   { get; set; }
    public string?  NroComprobante  { get; set; }
    public int?     FkColor         { get; set; }
    public DateTime? FechaMov       { get; set; }

    // Navegación
    public Producto? Producto { get; set; }
}
