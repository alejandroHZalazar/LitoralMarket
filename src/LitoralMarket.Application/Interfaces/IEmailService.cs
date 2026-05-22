namespace LitoralMarket.Application.Interfaces;

/// <summary>
/// Servicio de envío de correos electrónicos.
/// La configuración SMTP se lee de la tabla parametros (modulo='mail').
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Envía el correo de confirmación de pedido (pago contra reembolso).
    /// Devuelve <c>true</c> si el email fue enviado exitosamente.
    /// </summary>
    Task<bool> EnviarConfirmacionPedidoAsync(int pedidoId);

    /// <summary>
    /// Envía el correo con el link de pago de MercadoPago.
    /// Devuelve <c>true</c> si el email fue enviado exitosamente.
    /// </summary>
    Task<bool> EnviarLinkMercadoPagoAsync(int pedidoId, int cobroId);

    /// <summary>
    /// Envía una notificación interna al administrador cuando llega un nuevo pedido.
    /// </summary>
    Task EnviarNotificacionAdminAsync(int pedidoId);
}
