using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

/// <summary>
/// Administración de productos con variantes (medidas y/o colores). Cada variante es un producto común más
/// (fila de Productos con su propio stock, precio y costo) enlazado a una ficha principal.
/// Todo se hace solo con ecommerce/HabilitarVariantesProducto = '1'.
/// </summary>
public interface IVarianteAdminService
{
    /// <summary>true si las tablas de variantes existen en esta base (se corrió sql/variantes_01_tablas.sql).</summary>
    Task<bool> TablasListasAsync();

    // ── Maestros ───────────────────────────────────────────────────────────
    Task<List<AtributoVarianteDto>> ListarMedidasAsync(bool soloActivas = false);
    Task<List<AtributoVarianteDto>> ListarColoresAsync(bool soloActivas = false);
    Task<ResultadoAtributo> GuardarMedidaAsync(int? id, string nombre, int orden, bool activa);
    Task<ResultadoAtributo> GuardarColorAsync(int? id, string nombre, string? codigoHex, int orden, bool activa);
    /// <summary>Elimina solo si ninguna variante lo usa; si está en uso devuelve un error (hay que deshabilitarlo).</summary>
    Task<ResultadoAtributo> EliminarMedidaAsync(int id);
    Task<ResultadoAtributo> EliminarColorAsync(int id);

    // ── Fichas ─────────────────────────────────────────────────────────────
    /// <summary>Estado de variantes de un producto (TieneVariantes = false si es un producto simple).</summary>
    Task<FichaVariantesDto> ObtenerFichaAsync(int productoId);

    /// <summary>
    /// Qué pasaría con las variantes existentes si el costo/precio común pasara a los valores indicados:
    /// cuántas lo siguen (se actualizarían) y cuáles tienen precio propio (no se tocarían).
    /// </summary>
    Task<VistaPreviaPreciosDto> VistaPreviaPrecioComunAsync(int productoId, decimal nuevoCosto, decimal nuevoPrecio);

    /// <summary>
    /// Crea o actualiza la ficha principal y sus variantes en UNA transacción: valida, guarda el producto principal
    /// (<see cref="IProductoAdminService"/>), genera las descripciones, copia a las variantes los atributos comunes,
    /// aplica los precios y habilita/deshabilita combinaciones. Si algo no valida no se escribe nada.
    /// </summary>
    Task<ResultadoVariantes> GuardarFichaAsync(ProductoAdminDto principal, GuardarVariantesRequest request);
}
