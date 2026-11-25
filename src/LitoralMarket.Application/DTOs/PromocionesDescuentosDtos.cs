namespace LitoralMarket.Application.DTOs;

// ── ABM de promociones por cantidad / porcentaje (mismos datos que el ABM de Comercial Web) ──

public class PromoReglaListDto
{
    public int       Id            { get; set; }
    public string    Nombre        { get; set; } = string.Empty;
    public string    Tipo          { get; set; } = string.Empty;   // PORC | NXM
    public string    Resumen       { get; set; } = string.Empty;   // "20 % de descuento", "3x2"
    public DateTime  Desde         { get; set; }
    public DateTime  Hasta         { get; set; }
    public bool      Activa        { get; set; }
    /// <summary>Activa y dentro del periodo (hora Argentina).</summary>
    public bool      Vigente       { get; set; }
    public int       CantProductos { get; set; }
    /// <summary>Solo NxM: acumula las variantes de una misma ficha.</summary>
    public bool      AgrupaVariantes { get; set; }
}

public class PromoReglaProductoDto
{
    public int      ProductoId   { get; set; }
    public string?  CodProveedor { get; set; }
    public string?  CodBarras    { get; set; }
    public string   Descripcion  { get; set; } = string.Empty;
    public string?  Proveedor    { get; set; }
    public string?  Rubro        { get; set; }
    /// <summary>Precio vigente (misma fuente que el catalogo). Nunca se persiste.</summary>
    public decimal  PrecioActual { get; set; }
}

public class PromoReglaDetalleDto
{
    public int       Id          { get; set; }
    public string    Nombre      { get; set; } = string.Empty;
    public string    Tipo        { get; set; } = "PORC";
    public decimal?  Porcentaje  { get; set; }
    public int?      Lleva       { get; set; }
    public int?      Paga        { get; set; }
    /// <summary>Solo NxM: acumula las cantidades de las variantes de una misma ficha (false = cada producto por separado).</summary>
    public bool      AgrupaVariantes { get; set; }
    public DateTime  Desde       { get; set; }
    public DateTime  Hasta       { get; set; }
    public bool      Activa      { get; set; } = true;
    public List<int> ProductoIds { get; set; } = new();
    /// <summary>Solo lectura: productos con su precio actual (para la grilla del ABM).</summary>
    public List<PromoReglaProductoDto> Productos { get; set; } = new();
}

public class PromoBusquedaProductosDto
{
    public int Total { get; set; }
    public List<PromoReglaProductoDto> Items { get; set; } = new();
}

// ── Promociones aplicadas al carrito (solo lectura, para mostrar) ──

/// <summary>Promocion que se esta aplicando a una linea del carrito, ya calculada por el servidor.</summary>
public class PromoLineaCarritoDto
{
    public int      ProductoId     { get; set; }
    public int      ReglaId        { get; set; }
    public string   Nombre         { get; set; } = string.Empty;
    public string   Tipo           { get; set; } = string.Empty;
    public decimal? Porcentaje     { get; set; }
    public int?     Lleva          { get; set; }
    public int?     Paga           { get; set; }
    public decimal  CantBonificada { get; set; }
    public decimal  Ahorro         { get; set; }
    /// <summary>"20 % de descuento" / "Promo 3x2: 1 unidad bonificada".</summary>
    public string   Texto          { get; set; } = string.Empty;
}

/// <summary>Aviso informativo: con 1 unidad mas se completa un grupo NxM. No modifica ningun importe.</summary>
public class PromoProximaCarritoDto
{
    public int    ProductoId { get; set; }
    public string Texto      { get; set; } = string.Empty;
}

public class PromoCarritoResultado
{
    public Dictionary<int, PromoLineaCarritoDto> PorProducto { get; set; } = new();
    public Dictionary<int, PromoProximaCarritoDto> Proximas  { get; set; } = new();
}
