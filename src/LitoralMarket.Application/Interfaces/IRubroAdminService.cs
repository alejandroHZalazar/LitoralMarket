using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

/// <summary>Fila del ABM de rubros: un rubro o subrubro con su ubicación en el árbol.</summary>
public class RubroAdminDto
{
    public int    Id            { get; set; }
    public string Descripcion   { get; set; } = string.Empty;
    public int?   PadreId       { get; set; }
    public string Ruta          { get; set; } = string.Empty;
    public int    Nivel         { get; set; } = 1;
    /// <summary>Productos activos asignados directamente a este rubro (sin contar subrubros).</summary>
    public int    CantProductos { get; set; }
}

public interface IRubroAdminService
{
    /// <summary>Rubros y subrubros en orden de árbol, filtrados por texto sobre la ruta completa.</summary>
    Task<List<RubroAdminDto>> BuscarAsync(string valor);

    /// <summary>Alta (id = 0) o modificación. <paramref name="padreId"/> null = rubro principal.</summary>
    Task<(bool Ok, string? Error)> GuardarAsync(int id, string descripcion, int? padreId);

    /// <summary>
    /// Elimina el rubro. La base lo impide si tiene subrubros o productos asignados
    /// (incluidos los dados de baja) y se informa el motivo.
    /// </summary>
    Task<(bool Ok, string? Error)> EliminarAsync(int id);
}
