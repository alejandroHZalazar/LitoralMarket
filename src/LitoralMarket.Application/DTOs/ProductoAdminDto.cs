using System.ComponentModel.DataAnnotations;

namespace LitoralMarket.Application.DTOs;

/// <summary>
/// Para qué se busca un producto en la administración. Solo importa con ecommerce/HabilitarVariantesProducto = 1;
/// apagado, todos los contextos devuelven lo mismo que siempre.
/// </summary>
public enum ContextoBusquedaProducto
{
    /// <summary>Todo, sin distinguir fichas ni variantes (comportamiento histórico).</summary>
    Todos = 0,
    /// <summary>ABM de productos: fichas y productos simples (las variantes se editan desde su ficha; si el texto coincide con una variante se muestra su ficha).</summary>
    Administracion = 1,
    /// <summary>Ingreso y ajuste de stock: productos simples y variantes (la ficha no tiene stock propio).</summary>
    ParaStock = 2,
    /// <summary>Selección para el catálogo (Inicio): fichas y productos simples; las variantes no se eligen sueltas.</summary>
    ParaCatalogo = 3
}

/// <summary>Ítem para ingreso masivo de stock.</summary>
/// <param name="ProductoId">Id del producto.</param>
/// <param name="Cantidad">Unidades a ingresar (entero positivo).</param>
/// <param name="Observacion">Descripción opcional del movimiento.</param>
public record IngresoItemRequest(int ProductoId, int Cantidad, string? Observacion = null);

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
    /// <summary>Ruta completa del rubro asignado, ej. "Rubro padre &gt; Subrubro". Solo lectura (detalle/grilla).</summary>
    public string? RubroRuta       { get; set; }

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

    /// <summary>Cantidad mínima permitida por venta (Productos.cantidadMinimaVenta). En BD es NOT NULL DEFAULT 1.00.</summary>
    public decimal CantidadMinimaVenta { get; set; } = 1;

    public bool    EsPromocion     { get; set; }
    public bool    Fraccionado     { get; set; }
    public bool    Dolarizado      { get; set; }
    public bool    Baja            { get; set; }

    // ── Variantes (solo se completan con ecommerce/HabilitarVariantesProducto = 1) ──
    /// <summary>El producto es una ficha principal que agrupa variantes.</summary>
    public bool    EsFicha         { get; set; }
    /// <summary>Cantidad de variantes de la ficha (si <see cref="EsFicha"/>).</summary>
    public int     CantVariantes   { get; set; }
    /// <summary>Si el producto es una variante: Productos.id de su ficha.</summary>
    public int?    FkPrincipal     { get; set; }
}
