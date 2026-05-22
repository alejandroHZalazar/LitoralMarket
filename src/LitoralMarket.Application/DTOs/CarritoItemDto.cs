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
}
