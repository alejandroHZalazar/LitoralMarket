using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IDireccionEntregaRepository
{
    Task<List<DireccionEntregaDto>> ObtenerActivasAsync();
    Task<DireccionEntregaDto?> ObtenerPorIdAsync(int id);
}
