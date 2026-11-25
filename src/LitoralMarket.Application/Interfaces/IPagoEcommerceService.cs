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
    /// Crea el cobro de transferencia con un importe único (total menos $0,01–$0,99) y su vencimiento.
    /// Si el pedido ya tiene un cobro de transferencia vigente, lo devuelve sin crear otro.
    /// </summary>
    Task<ResultadoPagoDto> CrearCobroTransferenciaAsync(int pedidoId);

    /// <summary>
    /// Cancela los cobros de transferencia pendientes cuyo vencimiento pasó (liberan su importe).
    /// Devuelve la cantidad cancelada.
    /// </summary>
    Task<int> CancelarTransferenciasVencidasAsync();

    /// <summary>
    /// Procesa el webhook de MercadoPago: confirma el cobro y el pedido si el pago fue aprobado.
    /// </summary>
    Task ProcesarNotificacionMercadoPagoAsync(string paymentId);

    /// <summary>
    /// Punto único de confirmación de un cobro aprobado (webhook, transferencias, asignación manual).
    /// Marca el cobro como aprobado con un UPDATE condicional y, en la misma transacción, confirma el
    /// pedido y descuenta el stock; solo entonces envía los emails. Si otro proceso ya lo aprobó,
    /// devuelve false sin hacer nada (idempotente). Si falla la confirmación del pedido, se revierte todo.
    /// </summary>
    /// <param name="soloPendiente">
    /// true: el cobro debe estar 'pendiente'. false: alcanza con que no esté 'aprobado'
    /// (pago aprobado después de uno rechazado, o cobro vencido asignado a mano).
    /// </param>
    Task<bool> ConfirmarCobroAsync(int cobroId, int pedidoId, string? mpPaymentId, string? mpStatus,
        string origen, bool soloPendiente = true);

    /// <summary>
    /// Retorna el cobro activo de un pedido (si existe).
    /// </summary>
    Task<CobroEcommerceDto?> ObtenerCobroPorPedidoAsync(int pedidoId);

    /// <summary>
    /// Indica qué métodos de pago están habilitados (reembolso / mercadopago / transferencia).
    /// La transferencia exige además tener cargado el alias o el CVU de destino.
    /// </summary>
    Task<(bool reembolso, bool mercadoPago, bool transferencia)> ObtenerMetodosHabilitadosAsync();
}
