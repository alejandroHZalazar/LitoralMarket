using LitoralMarket.Application.DTOs;
using LitoralMarket.Domain.Entities;

namespace LitoralMarket.Application.Interfaces;

public interface IProductoRepository
{
    Task<List<ProductoDto>> ObtenerPorRubroAsync(int rubroId, bool incluirSinStock, int pagina, int porPagina);
    Task<List<ProductoDto>> BuscarAsync(string termino, bool incluirSinStock, int pagina, int porPagina);

    /// <summary>
    /// Últimos productos creados, ordenados por Id descendente (no hay columna de
    /// fecha de alta en el esquema). Excluye dados de baja y respeta el criterio de
    /// stock. Trae solo <paramref name="cantidad"/> filas y sin cargar el blob de imagen.
    /// </summary>
    Task<List<ProductoDto>> ObtenerUltimosAsync(int cantidad, bool incluirSinStock);
    Task<ProductoDto?> ObtenerPorIdAsync(int id);
    Task<List<Rubro>> ObtenerRubrosAsync();
    Task<int> ContarPorRubroAsync(int rubroId, bool incluirSinStock);
    Task<int> ContarBusquedaAsync(string termino, bool incluirSinStock);
}
