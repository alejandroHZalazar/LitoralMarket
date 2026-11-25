namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Ficha principal de un producto con variantes. Tabla <c>productosPrincipales</c>.
/// <see cref="FkProducto"/> es el <c>Productos.id</c> de la ficha: su nombre, descripción larga, imágenes, rubro
/// y proveedor son los de esa fila de <c>Productos</c>. Si <see cref="PreciosIguales"/>, su costo y precio
/// (sus propias filas de costosProductos / preciosProductos) son los valores comunes de todas las variantes.
/// Su stock no se usa (siempre 0): el stock es de cada variante.
/// </summary>
public class ProductoPrincipal
{
    public int      FkProducto     { get; set; }
    public bool     UsaMedidas     { get; set; }
    public bool     UsaColores     { get; set; }
    public bool     PreciosIguales { get; set; } = true;
    public DateTime FechaAlta      { get; set; } = DateTime.Now;

    public Producto? Producto { get; set; }
    public ICollection<ProductoVariante> Variantes { get; set; } = new List<ProductoVariante>();
}
