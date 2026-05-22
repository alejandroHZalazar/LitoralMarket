using LitoralMarket.Domain.Entities;

namespace LitoralMarket.Application.Interfaces;

public interface IParametrosAdminService
{
    Task<List<Parametro>> ListarAsync();
    Task<Parametro?> ObtenerPorIdAsync(int id);
    Task CrearAsync(Parametro p);
    Task ActualizarAsync(int id, string? modulo, string? nombre, string? valor, byte[]? imagen, bool quitarImagen);
    Task EliminarAsync(int id);
}
