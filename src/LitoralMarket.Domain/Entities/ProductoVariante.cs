namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Variante de un producto: cada una ES una fila de <c>Productos</c> (con su propio código de barras, código de
/// proveedor, stock, precio y costo) enlazada a su ficha por esta tabla (<c>productosVariantes</c>).
/// Como para el resto del sistema es un producto común, el carrito, el pedido, la venta, el stock y los
/// reportes la identifican por <see cref="FkProducto"/> sin ningún cambio.
/// </summary>
public class ProductoVariante
{
    /// <summary>Productos.id de la variante. Es la clave primaria.</summary>
    public int FkProducto  { get; set; }
    /// <summary>productosPrincipales.fk_producto (la ficha).</summary>
    public int FkPrincipal { get; set; }
    /// <summary>variantesMedidas.id; 0 = la ficha no usa medidas.</summary>
    public int FkMedida    { get; set; }
    /// <summary>variantesColores.id; 0 = la ficha no usa colores.</summary>
    public int FkColor     { get; set; }
    /// <summary>false = combinación deshabilitada (además Productos.baja = 1).</summary>
    public bool Activa     { get; set; } = true;
    public int  Orden      { get; set; }
    public DateTime FechaAlta { get; set; } = DateTime.Now;

    public Producto? Producto { get; set; }
    public ProductoPrincipal? Principal { get; set; }
}
