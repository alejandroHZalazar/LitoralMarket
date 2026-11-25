namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Línea de venta del sistema comercial (tabla <c>ventasDetalle</c>, compartida con Comercial Web).
/// </summary>
public class VentaDetalle
{
    public long     Linea        { get; set; }
    public long?    FkVenta      { get; set; }
    public int?     FkProducto   { get; set; }
    public string?  CodBarras    { get; set; }
    public string?  CodProveedor { get; set; }
    public string?  Descripcion  { get; set; }
    /// <summary>Precio unitario base, antes de descuento/recargo e IVA.</summary>
    public decimal? PrecioSinIva { get; set; }
    /// <summary>Precio unitario final (con desc/rec e IVA).</summary>
    public decimal? PrecioConIva { get; set; }
    public decimal? Cantidad     { get; set; }
    /// <summary>Costo TOTAL de la línea: cantidad × costo actual del producto.</summary>
    public decimal? Costo        { get; set; }
    public decimal? Subtotal     { get; set; }
    /// <summary>Porcentaje de descuento (null si no hay).</summary>
    public decimal? Descuento    { get; set; }
    /// <summary>Porcentaje de recargo (null si no hay).</summary>
    public decimal? Recargo      { get; set; }
    /// <summary>A pesar del nombre, es un precio UNITARIO: con desc/rec y sin IVA (igual que Comercial Web).</summary>
    public decimal? SubtotalSinIva { get; set; }
}
