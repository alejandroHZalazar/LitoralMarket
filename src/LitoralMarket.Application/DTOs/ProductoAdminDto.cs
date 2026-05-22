using System.ComponentModel.DataAnnotations;

namespace LitoralMarket.Application.DTOs;

public class ProductoAdminDto
{
    public int     Id              { get; set; }

    [MaxLength(50)]
    public string? CodProveedor    { get; set; }

    [MaxLength(50)]
    public string? CodBarras       { get; set; }

    [Required, MaxLength(300)]
    public string? Descripcion     { get; set; }

    public string? DescripcionLarga { get; set; }

    public int?    FkRubro         { get; set; }
    public string? RubroNombre     { get; set; }

    public int?    FkProveedor     { get; set; }
    public string? ProveedorNombre { get; set; }

    public decimal Iva             { get; set; }

    /// <summary>Precio que cobra el proveedor — se usa para calcular Costo y Lista, no se persiste por separado.</summary>
    public decimal PrecioProveedor { get; set; }

    /// <summary>Precio de costo → costosProductos.costo</summary>
    public decimal PrecioCosto     { get; set; }

    /// <summary>Precio de lista → preciosProductos.precio</summary>
    public decimal PrecioLista     { get; set; }

    public decimal StockActual     { get; set; }
    public decimal StockMinimo     { get; set; }

    public bool    EsPromocion     { get; set; }
    public bool    Fraccionado     { get; set; }
    public bool    Dolarizado      { get; set; }
    public bool    Baja            { get; set; }
}
