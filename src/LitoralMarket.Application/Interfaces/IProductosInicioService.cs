using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

/// <summary>Administración de la selección manual de productos del Inicio.</summary>
public interface IProductosInicioService
{
    /// <summary>Selección guardada, en el orden del administrador, con el estado de cada producto.</summary>
    Task<List<ProductoInicioItemDto>> ObtenerSeleccionAsync();

    /// <summary>
    /// Reemplaza la selección completa por la lista dada (la posición es el orden). Máximo 20, sin repetidos;
    /// los productos que se agregan deben existir y no estar dados de baja. Todo o nada.
    /// </summary>
    Task<GuardarSeleccionInicioResultado> GuardarSeleccionAsync(IReadOnlyList<int> productoIdsEnOrden);
}
