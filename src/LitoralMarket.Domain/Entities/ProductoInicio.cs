namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Producto elegido por el administrador para la página de inicio (tabla productosInicio).
/// Solo se usa con el parámetro ecommerce/ProductosInicioConfigurables = 1. El orden es la
/// posición en la que se muestra; fk_producto es único (un producto no se repite).
/// </summary>
public class ProductoInicio
{
    public int Id { get; set; }
    public int FkProducto { get; set; }
    public int Orden { get; set; }
    public System.DateTime FechaAlta { get; set; }
}
