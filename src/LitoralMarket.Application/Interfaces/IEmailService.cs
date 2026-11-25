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
    /// Envía al cliente las instrucciones para pagar por transferencia (destino, importe exacto y plazo).
    /// Devuelve <c>true</c> si el email fue enviado exitosamente.
    /// </summary>
    Task<bool> EnviarInstruccionesTransferenciaAsync(int pedidoId, int cobroId);

    /// <summary>
    /// Avisa a los administradores de transferencias recibidas que no se pudieron asociar solas a un pedido.
    /// Cada línea es un texto ya armado (sin HTML).
    /// </summary>
    Task EnviarAlertaTransferenciasAdminAsync(IReadOnlyList<string> detalles);

    /// <summary>
    /// Avisa a los administradores que un pedido pagado no se pudo confirmar porque falló el registro
    /// de la venta (el pedido quedó pendiente de pago y se puede reintentar).
    /// </summary>
    Task EnviarAlertaVentaFallidaAsync(int pedidoId, string motivo);

    /// <summary>
    /// Envía una notificación interna al administrador cuando llega un nuevo pedido.
    /// </summary>
    Task EnviarNotificacionAdminAsync(int pedidoId);

    /// <summary>
    /// Envía a los administradores (mismos destinatarios que la notificación de pedido
    /// nuevo) una solicitud de registro/acceso con los datos que cargó el visitante.
    /// Devuelve <c>true</c> si el email fue enviado exitosamente.
    /// </summary>
    Task<bool> EnviarSolicitudRegistroAsync(LitoralMarket.Application.DTOs.SolicitudRegistroDto datos);
}
