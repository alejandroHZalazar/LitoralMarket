using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface ICarritoService
{
    Task<int> ObtenerOCrearBorradorAsync(string guestToken, int? clienteId);

    /// <summary>
    /// Busca el pedido borrador existente del cliente/invitado sin crear uno nuevo.
    /// Null si no tiene ninguno. Pensado para mostrar el contador del carrito (header)
    /// en páginas que no deben generar un pedido vacío por el solo hecho de visitarlas.
    /// </summary>
    Task<int?> BuscarBorradorAsync(string guestToken, int? clienteId);
    Task AgregarItemAsync(int pedidoId, int productoId, decimal cantidad);
    Task QuitarItemAsync(int pedidoId, long lineaId);
    Task ActualizarCantidadAsync(int pedidoId, long lineaId, decimal cantidad);
    Task<List<CarritoItemDto>> ObtenerItemsAsync(int pedidoId);
    Task ActualizarPreciosAsync(int pedidoId);
    Task<int> ContarItemsAsync(int pedidoId);
    Task<decimal> ObtenerTotalAsync(int pedidoId);
    Task VaciarAsync(int pedidoId);

    /// <summary>
    /// Refresca precios de lista y costos desde la BD, y elimina los ítems
    /// cuyo stock actual es cero o negativo.
    /// Devuelve las descripciones de los productos eliminados (puede estar vacía).
    /// </summary>
    Task<List<string>> SincronizarCarritoAsync(int pedidoId);
}
