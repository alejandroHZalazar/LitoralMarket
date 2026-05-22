using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IPedidoService
{
    Task<(bool valido, List<string> errores)> ValidarStockAsync(int pedidoId);

    /// <summary>
    /// Guarda datos del cliente y dirección de entrega. Pasa el pedido a 'pendiente_pago'.
    /// NO descuenta stock todavía. Retorna el pedidoId.
    /// </summary>
    Task<int> PrepararPagoAsync(int pedidoId, CheckoutDto datos);

    /// <summary>
    /// Confirma el pedido: descuenta stock, pasa a 'confirmado'.
    /// Llamado después de que el pago fue aceptado.
    /// </summary>
    Task ConfirmarPagoAsync(int pedidoId);

    /// <summary>
    /// Modo "credenciales": combina PrepararPago + ConfirmarPago en un solo paso
    /// (borrador → confirmado) sin pasar por pantalla de pago ni crear CobroEcommerce.
    /// </summary>
    Task<int> ConfirmarDirectoAsync(int pedidoId, CheckoutDto datos);

    Task<PedidoResumenDto?> ObtenerResumenAsync(int pedidoId);
    Task<List<PedidoResumenDto>> ObtenerPorClienteAsync(int clienteId);

    /// <summary>
    /// Retorna pedidos ecommerce en estado 'pendiente_pago' o 'confirmado'
    /// de los últimos 3 meses asociados al guest token del navegador anónimo.
    /// </summary>
    Task<List<PedidoResumenDto>> ObtenerPorGuestTokenAsync(string guestToken);

    Task LimpiarBorradoresViejosAsync(int diasAntiguedad = 7);
}
