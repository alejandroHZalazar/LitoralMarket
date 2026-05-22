namespace LitoralMarket.Application.Interfaces;

/// <summary>
/// Genera el comprobante de pago en memoria (sin persistencia en disco ni en BD).
/// El PDF se crea, se usa (adjuntar a email u ofrecer descarga directa) y se descarta.
/// </summary>
public interface IPdfPagoService
{
    /// <summary>
    /// Genera el PDF del comprobante y retorna los bytes en memoria.
    /// No escribe nada en disco ni en base de datos.
    /// </summary>
    Task<byte[]> GenerarComprobanteAsync(int pedidoId, int cobroId);

    /// <summary>
    /// Genera el comprobante sin información de pago ni estado del pedido.
    /// Usado en modo "credenciales" donde no existe CobroEcommerce.
    /// </summary>
    Task<byte[]> GenerarComprobanteSinPagoAsync(int pedidoId);
}
