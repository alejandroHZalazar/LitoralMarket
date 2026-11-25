namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Cabecera de venta del sistema comercial (tabla <c>ventas</c>, compartida con Comercial Web).
/// Solo se mapean las columnas que escribe Comercial Web al grabar una venta
/// (VentaService.GrabarVentaAsync); factura/fFactura/fk_condIva no se tocan.
/// IVA, descuento, recargo e impuesto son PORCENTAJES, no importes.
/// </summary>
public class Venta
{
    /// <summary>Autoincremental: es el número de venta (no hay talonario).</summary>
    public long      Id         { get; set; }
    public DateTime? Fecha      { get; set; }
    public decimal?  TotalVenta { get; set; }
    public decimal?  TotalCosto { get; set; }
    public int?      FkCliente  { get; set; }
    public int?      FkCajero   { get; set; }
    public decimal?  Iva        { get; set; }
    public decimal?  Descuento  { get; set; }
    public decimal?  Recargo    { get; set; }
    public int?      FkVendedor { get; set; }
    public decimal?  Comision   { get; set; }
    public decimal?  Impuesto   { get; set; }
}
