using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IPagoEcommerceService
{
    /// <summary>
    /// Procesa pago contra reembolso: crea el cobro, confirma el pedido y descuenta stock.
    /// </summary>
    Task<ResultadoPagoDto> ProcesarReembolsoAsync(int pedidoId);

    /// <summary>
    /// Crea la preferencia de pago en MercadoPago y devuelve el link de pago.
    /// </summary>
    Task<ResultadoPagoDto> CrearPreferenciaMercadoPagoAsync(int pedidoId);

    /// <summary>
    /// Procesa el webhook de MercadoPago: confirma el cobro y el pedido si el pago fue aprobado.
    /// </summary>
    Task ProcesarNotificacionMercadoPagoAsync(string paymentId);

    /// <summary>
    /// Retorna el cobro activo de un pedido (si existe).
    /// </summary>
    Task<CobroEcommerceDto?> ObtenerCobroPorPedidoAsync(int pedidoId);

    /// <summary>
    /// Indica si los métodos de pago están habilitados (reembolso / mercadopago).
    /// </summary>
    Task<(bool reembolso, bool mercadoPago)> ObtenerMetodosHabilitadosAsync();
}
