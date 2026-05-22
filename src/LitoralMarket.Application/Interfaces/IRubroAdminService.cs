namespace LitoralMarket.Application.Interfaces;

public interface IRubroAdminService
{
    Task<List<(int Id, string Descripcion, int CantProductos)>> BuscarAsync(string valor);
    Task<(int Id, string Descripcion)?> ObtenerPorIdAsync(int id);
    Task<int>    CrearAsync(string descripcion);
    Task         ActualizarAsync(int id, string descripcion);

    /// <summary>
    /// Elimina el rubro si no tiene productos activos asociados.
    /// Devuelve true si se eliminó, false si tiene productos.
    /// </summary>
    Task<bool>   EliminarAsync(int id);
}
