using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IProductoAdminService
{
    /// <summary>Busca productos activos. tipo: 0=CodProveedor, 1=CodBarras, 2=Descripcion.</summary>
    Task<List<ProductoAdminDto>> BuscarAsync(int tipo, string valor);

    Task<ProductoAdminDto?> ObtenerPorIdAsync(int id);
    Task<byte[]?>           ObtenerImagenAsync(int id);

    /// <summary>Crea producto + stock + precio + costo. Devuelve el Id generado.</summary>
    Task<int> CrearAsync(ProductoAdminDto dto, byte[]? imagen);

    Task ActualizarAsync(ProductoAdminDto dto, byte[]? imagen);

    /// <summary>Baja lógica (Baja = true).</summary>
    Task BajaLogicaAsync(int id);

    /// <summary>
    /// Incrementa el stock del producto y registra un movimiento de ingreso (TipoMovimiento = 1).
    /// Devuelve el stock resultante.
    /// </summary>
    Task<decimal> IngresoStockAsync(int productoId, decimal cantidad, string? observacion);

    /// <summary>
    /// Ingreso masivo de stock: actualiza varios productos en una única transacción.
    /// Lanza <see cref="InvalidOperationException"/> si algún producto no existe o la cantidad es inválida.
    /// </summary>
    Task IngresoStockMasivoAsync(IEnumerable<IngresoItemRequest> items);
}
