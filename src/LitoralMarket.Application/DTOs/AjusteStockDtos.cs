namespace LitoralMarket.Application.DTOs;

/// <summary>Filtro de la grilla de ajuste de stock. El rubro incluye sus subrubros.</summary>
public class AjusteStockFiltro
{
    public int?    ProveedorId  { get; set; }
    public int?    RubroId      { get; set; }
    /// <summary>0 = cód. proveedor, 1 = cód. barras, 2 = descripción (solo con <see cref="Texto"/>).</summary>
    public int     Tipo         { get; set; } = 2;
    public string? Texto        { get; set; }
    public bool    OcultarBajas { get; set; } = true;
}

public class AjusteStockItemDto
{
    public int     Id           { get; set; }
    public string? CodProveedor { get; set; }
    public string? CodBarras    { get; set; }
    public string  Descripcion  { get; set; } = string.Empty;
    public decimal Stock        { get; set; }
    public bool    Baja         { get; set; }
}

public class AjusteStockBusquedaResultado
{
    public List<AjusteStockItemDto> Items { get; set; } = new();
    /// <summary>True si hay más productos que el tope por búsqueda (hay que afinar el filtro).</summary>
    public bool Truncado { get; set; }
    public int  Limite   { get; set; }
}

/// <summary>Stock que el usuario vio al buscar y stock que quiere dejar.</summary>
public record AjusteStockLinea(int ProductoId, decimal StockLeido, decimal StockNuevo);

public record AjusteStockConflicto(int ProductoId, decimal StockActual);

public class AjusteStockResultado
{
    public bool    Ok        { get; set; }
    public int     Ajustados { get; set; }
    public string? Error     { get; set; }
    /// <summary>Productos cuyo stock cambió desde que se buscó: no se aplicó ningún ajuste.</summary>
    public List<AjusteStockConflicto> Conflictos { get; set; } = new();

    public static AjusteStockResultado Fallo(string error) => new() { Ok = false, Error = error };
}
