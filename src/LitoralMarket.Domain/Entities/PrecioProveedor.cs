namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Precio del proveedor por producto (tabla <c>preciosProveedores</c>, compartida con Comercial Web, que la
/// necesita para abrir el detalle de un producto). Una fila por producto.
/// </summary>
public class PrecioProveedor
{
    public int      Id         { get; set; }
    public int?     FkProducto { get; set; }
    public decimal? Precio     { get; set; }
}
