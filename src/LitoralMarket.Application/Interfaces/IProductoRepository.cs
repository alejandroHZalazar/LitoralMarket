using LitoralMarket.Application.DTOs;

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
    /// <summary>
    /// Productos que eligió el administrador para el Inicio (tabla productosInicio), en su orden y hasta
    /// <paramref name="maximo"/>. Solo los visibles en el catálogo: sin dados de baja y, según el parámetro, con stock.
    /// </summary>
    Task<List<ProductoDto>> ObtenerSeleccionInicioAsync(bool incluirSinStock, int maximo);
    Task<ProductoDto?> ObtenerPorIdAsync(int id);

    /// <summary>
    /// Productos de una lista de ids (los de una promoción), con los mismos filtros del catálogo (baja, stock) y ordenados por
    /// descripción. Con variantes habilitadas, cada variante se muestra como su ficha (una sola vez). Devuelve la página y el total.
    /// </summary>
    Task<(List<ProductoDto> Items, int Total)> BuscarPorIdsAsync(IReadOnlyCollection<int> ids, bool incluirSinStock, int pagina, int porPagina);

    /// <summary>Con variantes habilitadas: id de la ficha a la que pertenece una variante activa (null si no es variante o el parámetro está apagado).</summary>
    Task<int?> ObtenerPrincipalDeVarianteAsync(int productoId);

    /// <summary>Con variantes habilitadas: las variantes activas de una ficha para el detalle (null si no es ficha o el parámetro está apagado).</summary>
    Task<FichaCatalogoDto?> ObtenerVariantesAsync(int principalId);

    /// <summary>Metadatos (sin blob) de las imágenes activas de un producto, principal primero.</summary>
    Task<List<ProductoImagenDto>> ObtenerImagenesAsync(int productoId);

    /// <summary>
    /// Todos los rubros y subrubros, ordenados en profundidad (cada padre seguido de sus
    /// hijos) con ruta y nivel. Cacheado en memoria unos minutos.
    /// </summary>
    Task<List<RubroNodoDto>> ObtenerArbolRubrosAsync();

    /// <summary>
    /// Rubros y subrubros que deben aparecer en el menú: solo los que tienen productos
    /// disponibles para el catálogo (no dados de baja y, según el parámetro, con stock),
    /// más los padres necesarios para llegar a ellos. Mismo orden que el árbol. Cacheado.
    /// </summary>
    Task<List<RubroNodoDto>> ObtenerMenuRubrosAsync(bool incluirSinStock);

    /// <summary>Descarta el árbol cacheado (usar tras crear/editar/eliminar rubros).</summary>
    void InvalidarCacheRubros();

    Task<int> ContarPorRubroAsync(int rubroId, bool incluirSinStock);
    Task<int> ContarBusquedaAsync(string termino, bool incluirSinStock);
}
