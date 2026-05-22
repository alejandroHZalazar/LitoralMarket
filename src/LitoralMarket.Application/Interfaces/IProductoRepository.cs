using LitoralMarket.Application.DTOs;
using LitoralMarket.Domain.Entities;

namespace LitoralMarket.Application.Interfaces;

public interface IProductoRepository
{
    Task<List<ProductoDto>> ObtenerPorRubroAsync(int rubroId, bool incluirSinStock, int pagina, int porPagina);
    Task<List<ProductoDto>> BuscarAsync(string termino, bool incluirSinStock, int pagina, int porPagina);
    Task<ProductoDto?> ObtenerPorIdAsync(int id);
    Task<List<Rubro>> ObtenerRubrosAsync();
    Task<int> ContarPorRubroAsync(int rubroId, bool incluirSinStock);
    Task<int> ContarBusquedaAsync(string termino, bool incluirSinStock);
}
