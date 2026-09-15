namespace LitoralMarket.Application.DTOs;

public class CarritoItemDto
{
    public long LineaId { get; set; }       // PK de PedidoDetalle (campo Linea)
    public int ProductoId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string? Imagen { get; set; }
    public decimal Precio { get; set; }
    public decimal Cantidad { get; set; }
    public decimal Subtotal { get; set; }   // viene calculado desde el servicio
    /// <summary>Cantidad mínima de venta del producto (Productos.cantidadMinimaVenta). &lt;= 1 = sin restricción.</summary>
    public decimal CantidadMinimaVenta { get; set; } = 1;
    /// <summary>Si el producto admite cantidades no enteras (paso 0.01) cuando no hay cantidadMinimaVenta.</summary>
    public bool Fraccionado { get; set; }
}
