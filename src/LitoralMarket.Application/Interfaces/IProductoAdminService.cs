using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IProductoAdminService
{
    /// <summary>Busca productos activos. tipo: 0=CodProveedor, 1=CodBarras, 2=Descripcion.</summary>
    Task<List<ProductoAdminDto>> BuscarAsync(int tipo, string valor);

    Task<ProductoAdminDto?> ObtenerPorIdAsync(int id);

    /// <summary>Metadatos (sin blob) de las imágenes activas de un producto, principal primero.</summary>
    Task<List<ProductoImagenDto>> ObtenerImagenesAsync(int productoId);

    /// <summary>
    /// Reconcilia el conjunto de imágenes del producto con la lista recibida:
    /// inserta las nuevas (DataUrl), conserva las existentes (Id), da de baja las
    /// que no llegan, y garantiza exactamente una imagen principal. Transaccional.
    /// </summary>
    Task SincronizarImagenesAsync(int productoId, List<ImagenSyncItem> imagenes);

    /// <summary>Crea producto + stock + precio + costo. Devuelve el Id generado.</summary>
    Task<int> CrearAsync(ProductoAdminDto dto);

    Task ActualizarAsync(ProductoAdminDto dto);

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
