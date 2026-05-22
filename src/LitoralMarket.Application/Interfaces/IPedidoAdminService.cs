using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IPedidoAdminService
{
    /// <summary>Lista pedidos ecommerce con filtros opcionales. Por defecto últimos 20.</summary>
    Task<List<PedidoAdminDto>> ListarAsync(
        DateTime? desde,
        DateTime? hasta,
        string?   estado,
        int       take = 20);

    /// <summary>Detalle completo de un pedido (con ítems).</summary>
    Task<PedidoAdminDto?> ObtenerDetalleAsync(int id);

    /// <summary>
    /// Confirma manualmente un pedido en estado pendiente_pago:
    /// cambia estado → confirmado, descuenta stock y registra movimientos.
    /// </summary>
    Task ConfirmarManualAsync(int id);
}
