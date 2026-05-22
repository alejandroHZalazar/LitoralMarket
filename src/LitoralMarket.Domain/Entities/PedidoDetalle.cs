namespace LitoralMarket.Domain.Entities;

public class PedidoDetalle
{
    public long Linea { get; set; }
    public int? FkPedido { get; set; }
    public int? FkProducto { get; set; }
    public string? CodBarras { get; set; }
    public string? CodProveedor { get; set; }
    public string? Descripcion { get; set; }
    public decimal? PrecioSinIva { get; set; }
    public decimal? Cantidad { get; set; }
    public decimal? Subtotal { get; set; }
    public bool? Procesado { get; set; }
    public decimal? CantEntregada { get; set; }
    public decimal? PrecioOrig { get; set; }
    public decimal? Costo { get; set; }
    public decimal? PrecioConIva { get; set; }
    public int FkColor { get; set; }
    public string? Observ { get; set; }
    public decimal? Descuento { get; set; }
    public decimal? Recargo { get; set; }
    public decimal? SubtotalSinIva { get; set; }

    public Pedido? Pedido { get; set; }
    public Producto? Producto { get; set; }
}
