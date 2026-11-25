using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

/// <summary>
/// Promociones por cantidad (NxM) y porcentaje. Usa las mismas tablas, reglas y motor que Comercial Web: lo que se carga
/// alla se aplica aca y viceversa. Se activa con el parametro promociones/descuentos = '1' (el mismo de Comercial Web) y
/// las tablas creadas por su script; con otro valor, sin tablas o sin parametro no se consulta ninguna tabla nueva y el
/// comportamiento es exactamente el de siempre.
/// </summary>
public interface IPromocionesService
{
    /// <summary>Parametro promociones/descuentos = '1' y tablas creadas (cacheado 60 s).</summary>
    Task<bool> HabilitadoAsync();

    /// <summary>Las tablas existen (permite abrir el ABM aunque el parametro este apagado).</summary>
    Task<bool> TablasListasAsync();

    // ── Catalogo y detalle ──────────────────────────────────────────────────
    /// <summary>
    /// Completa en cada producto la promocion vigente que le corresponde (porcentaje con su precio promocional y/o NxM),
    /// sin consultas por producto: usa las reglas vigentes cacheadas. Sin efecto si el modulo esta apagado.
    /// </summary>
    Task AplicarACatalogoAsync(IReadOnlyList<ProductoDto> productos);

    /// <summary>
    /// Estado y datos de UNA promoción para la página a la que llevan los banners del carrusel: si está vigente (hora Argentina),
    /// su beneficio y condición, y los ids de los productos asociados (solo si está vigente). Lee la regla por id, sin cálculos.
    /// </summary>
    Task<PromoDestinoDto> ObtenerParaCatalogoAsync(int reglaId);

    // ── Carrito ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Recalcula las lineas del carrito (pedido borrador) con las promociones vigentes: descuento, precio y subtotal.
    /// El servidor decide siempre; el navegador solo envia producto y cantidad. Sin efecto si el modulo esta apagado.
    /// </summary>
    Task AplicarAlCarritoAsync(int pedidoId);

    /// <summary>Promociones que se estan aplicando (y avisos de "+1 unidad") para las lineas dadas, solo para mostrar.</summary>
    Task<PromoCarritoResultado> EvaluarAsync(IEnumerable<(int ProductoId, decimal Cantidad, decimal PrecioBase)> lineas);

    // ── Confirmacion ────────────────────────────────────────────────────────
    /// <summary>
    /// Fija las promociones en el pedido al pasar a pendiente de pago: recalcula, expande cada NxM en una linea cobrada y
    /// otra bonificada al 100 % (igual que Comercial Web), registra la auditoria y actualiza el total de productos.
    /// </summary>
    Task MaterializarPedidoAsync(int pedidoId);

    /// <summary>Copia la auditoria de promociones de las lineas del pedido a las lineas de la venta registrada.</summary>
    Task CopiarAuditoriaAVentaAsync(IReadOnlyList<(long PedidoLinea, long VentaLinea)> pares);

    // ── ABM ─────────────────────────────────────────────────────────────────
    Task<List<PromoReglaListDto>> ListarAsync();
    Task<PromoReglaDetalleDto?>   ObtenerAsync(int id);
    /// <exception cref="InvalidOperationException">Datos invalidos (el mensaje se muestra al usuario).</exception>
    Task<int>                     GuardarAsync(PromoReglaDetalleDto dto, string? usuario);
    Task                          EliminarAsync(int id);
    Task<bool>                    ToggleActivaAsync(int id);
    Task<PromoBusquedaProductosDto> BuscarProductosAsync(IEnumerable<int>? proveedores, IEnumerable<int>? rubros, string? texto, int max);
}
