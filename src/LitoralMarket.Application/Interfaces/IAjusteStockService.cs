using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IAjusteStockService
{
    /// <summary>Productos con su stock para ajustar (con tope por búsqueda).</summary>
    Task<AjusteStockBusquedaResultado> BuscarAsync(AjusteStockFiltro filtro);

    /// <summary>
    /// Fija el stock de cada línea y registra un movimiento AJUSTE ALTA/BAJA. Todo o nada: si el
    /// stock de algún producto cambió desde que se leyó, no se aplica ninguna línea.
    /// </summary>
    Task<AjusteStockResultado> AjustarAsync(IReadOnlyList<AjusteStockLinea> lineas);

    /// <summary>Marca o reactiva un producto (Productos.baja). False si no existe.</summary>
    Task<bool> CambiarBajaAsync(int productoId, bool baja);

    /// <summary>Decimales permitidos en el stock (parametros productos/decimalesCant).</summary>
    Task<int> DecimalesStockAsync();
}
