using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IProductoAdminService
{
    /// <summary>
    /// Busca productos activos. tipo: 0=CodProveedor, 1=CodBarras, 2=Descripcion.
    /// Con variantes habilitadas, <paramref name="contexto"/> decide si se ven las fichas, las variantes o ambas.
    /// </summary>
    Task<List<ProductoAdminDto>> BuscarAsync(int tipo, string valor,
        ContextoBusquedaProducto contexto = ContextoBusquedaProducto.Todos);

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
    /// <summary>
    /// Alta de producto. Con productos/generaCodigoBarras = '1' y sin código de barras NI de proveedor, asigna el mismo
    /// código automático a ambos (queda también en <paramref name="dto"/>). <paramref name="generarCodigo"/> = false lo
    /// evita (la ficha principal de un producto con variantes no lleva código).
    /// </summary>
    Task<int> CrearAsync(ProductoAdminDto dto, bool generarCodigo = true);

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
