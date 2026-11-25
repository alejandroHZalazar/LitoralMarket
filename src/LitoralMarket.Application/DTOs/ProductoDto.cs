namespace LitoralMarket.Application.DTOs;

public class ProductoDto
{
    public int Id { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string? DescripcionLarga { get; set; }
    /// <summary>true si el producto tiene imagen almacenada en BD (columna imagen IS NOT NULL).</summary>
    public bool TieneImagen { get; set; }
    /// <summary>URL relativa a la imagen servida bajo demanda. Nunca carga el blob en listings.</summary>
    public string? Imagen => TieneImagen ? $"/images/productos/{Id}" : null;
    public int? RubroId { get; set; }
    public string? RubroNombre { get; set; }
    public decimal Precio { get; set; }
    public decimal Stock { get; set; }
    public bool TieneStock => Stock > 0;
    public bool EsPromocion { get; set; }
    public bool Fraccionado { get; set; }
    /// <summary>Cantidad mínima permitida por venta (Productos.cantidadMinimaVenta). &lt;= 1 = sin restricción.</summary>
    public decimal CantidadMinimaVenta { get; set; } = 1;
    public string? CodBarras { get; set; }
    public string? CodProveedor { get; set; }
    public int? Iva { get; set; }

    // ── Promociones por cantidad / porcentaje (solo con promociones/descuentos = 1; apagado quedan sin valor) ──
    /// <summary>Precio con la promocion de porcentaje vigente aplicada (null = sin promocion de porcentaje).</summary>
    public decimal? PrecioPromo { get; set; }
    /// <summary>Porcentaje de la promocion que da <see cref="PrecioPromo"/>.</summary>
    public decimal? PromoPorcentaje { get; set; }
    /// <summary>Promocion por cantidad vigente: el cliente lleva <c>PromoLleva</c> y paga <c>PromoPaga</c> (null = no hay).</summary>
    public int? PromoLleva { get; set; }
    public int? PromoPaga { get; set; }
    /// <summary>La promocion por cantidad acumula las variantes de una misma ficha.</summary>
    public bool PromoAgrupa { get; set; }
    /// <summary>Etiqueta corta del distintivo de la tarjeta: "20% OFF", "Llevá 3, pagá 2", "Hasta 30% OFF". null = sin promocion.</summary>
    public string? PromoEtiqueta { get; set; }
    /// <summary>Condicion de la promocion para mostrar bajo el precio (ej. "Sumá 3 unidades combinando variantes"). null = nada que aclarar.</summary>
    public string? PromoCondicion { get; set; }
    /// <summary>true si el producto tiene alguna promocion vigente (para mostrar la insignia).</summary>
    public bool TienePromo => PrecioPromo.HasValue || PromoLleva.HasValue || PromoGenerica;
    /// <summary>Ficha con variantes cuyas variantes tienen promocion: se muestra "PROMO" sin detalle (cada variante tiene la suya).</summary>
    public bool PromoGenerica { get; set; }

    // ── Variantes (solo con ecommerce/HabilitarVariantesProducto = 1; apagado quedan en sus valores por defecto) ──
    /// <summary>true si es una ficha con variantes: en el catálogo se muestra "Desde $X" y se elige medida/color en el detalle.</summary>
    public bool TieneVariantes { get; set; }
    /// <summary>Cantidad de medidas distintas entre las variantes visibles (0 si no usa medidas).</summary>
    public int CantMedidas { get; set; }
    /// <summary>Cantidad de colores distintos entre las variantes visibles (0 si no usa colores).</summary>
    public int CantColores { get; set; }
}
