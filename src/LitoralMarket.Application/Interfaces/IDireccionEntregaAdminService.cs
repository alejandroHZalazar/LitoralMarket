using LitoralMarket.Domain.Entities;

namespace LitoralMarket.Application.Interfaces;

public interface IDireccionEntregaAdminService
{
    Task<List<DireccionEntrega>> ListarAsync();
    Task<DireccionEntrega?>      ObtenerPorIdAsync(int id);
    Task<int>                    CrearAsync(DireccionEntrega dto);
    Task                         ActualizarAsync(DireccionEntrega dto);
    Task                         ToggleActivoAsync(int id);
    Task                         EliminarAsync(int id);

    /// <summary>Mueve el registro una posición hacia arriba o abajo en el orden.</summary>
    Task                         MoverAsync(int id, bool subir);
}
