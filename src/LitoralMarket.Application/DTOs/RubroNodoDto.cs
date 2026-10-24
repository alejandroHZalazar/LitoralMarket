namespace LitoralMarket.Application.DTOs;

/// <summary>
/// Un rubro o subrubro dentro del árbol de rubros. La lista completa viene ordenada
/// "en profundidad" (cada padre seguido de sus hijos), lista para mostrar con sangría.
/// </summary>
public class RubroNodoDto
{
    public int     Id          { get; set; }
    public string  Descripcion { get; set; } = string.Empty;
    /// <summary>null = rubro principal.</summary>
    public int?    PadreId     { get; set; }
    /// <summary>Ruta completa, ej. "ACESSORIO INFANTIL &gt; SUBABY SRL".</summary>
    public string  Ruta        { get; set; } = string.Empty;
    /// <summary>1 = rubro principal, 2 = subrubro, ...</summary>
    public int     Nivel       { get; set; } = 1;
}
