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
    /// <summary>Cantidad mínima de venta del producto (Productos.cantidadMinimaVenta). &lt;= 1 = sin restricción.</summary>
    public decimal CantidadMinimaVenta { get; set; } = 1;
    /// <summary>Si el producto admite cantidades no enteras (paso 0.01) cuando no hay cantidadMinimaVenta.</summary>
    public bool Fraccionado { get; set; }
    /// <summary>
    /// Mayor cantidad que se puede pedir según el stock. Solo se completa con ecommerce/LimitarCantidadPorStock = 1;
    /// null = sin límite.
    /// </summary>
    public decimal? MaximoPermitido { get; set; }

    // ── Promociones (solo con promociones/descuentos = 1; apagado quedan sin valor) ──
    /// <summary>Precio normal por unidad cuando la linea tiene una promocion de porcentaje (para mostrarlo tachado).</summary>
    public decimal? PrecioOriginal { get; set; }
    /// <summary>Promocion aplicada a la linea: "20 % de descuento", "Promo 3x2: 1 unidad bonificada".</summary>
    public string? PromoTexto { get; set; }
    /// <summary>Importe que se deja de cobrar en la linea por la promocion.</summary>
    public decimal PromoAhorro { get; set; }
    /// <summary>Aviso informativo: "sumá 1 unidad más y llevás 3 pagando 2".</summary>
    public string? PromoProxima { get; set; }
}
