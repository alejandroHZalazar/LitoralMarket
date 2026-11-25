namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Vínculo pedido ↔ venta (tabla <c>pedidos_ventas</c>, compartida con Comercial Web).
/// UNIQUE (fk_pedido, fk_venta).
/// </summary>
public class PedidoVenta
{
    public int      Id       { get; set; }
    public int      FkPedido { get; set; }
    public long     FkVenta  { get; set; }
    public DateTime Fecha    { get; set; }
}
