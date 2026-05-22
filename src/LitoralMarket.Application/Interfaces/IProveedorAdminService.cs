using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IProveedorAdminService
{
    Task<List<ProveedorAdminDto>> BuscarAsync(string valor);
    Task<ProveedorAdminDto?>      ObtenerPorIdAsync(int id);
    Task<int>                     CrearAsync(ProveedorAdminDto dto);
    Task                          ActualizarAsync(ProveedorAdminDto dto);
    Task                          BajaLogicaAsync(int id);
}
