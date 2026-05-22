using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IProveedorRepository
{
    Task<List<ProveedorDto>> ObtenerActivosAsync();
    Task<ProveedorDto?>      ObtenerPorIdAsync(int id);
}
