using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Text;

namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Servicio de email usando MailKit.
/// La configuración SMTP se lee de la tabla parametros (modulo='mail').
/// Todos los PDFs se generan en memoria; nunca se escriben en disco ni en BD.
/// </summary>
public class EmailService : IEmailService
{
    private readonly AppDbContext          _db;
    private readonly IParametrosService    _params;
    private readonly IPdfPagoService       _pdf;
    private readonly IHttpClientFactory    _http;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        AppDbContext          db,
        IParametrosService    params_,
        IPdfPagoService       pdf,
        IHttpClientFactory    http,
        ILogger<EmailService> logger)
    {
        _db     = db;
        _params = params_;
        _pdf    = pdf;
        _http   = http;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────
    // Confirmación de pedido (pago contra reembolso o MP aprobado)
    // ──────────────────────────────────────────────────────────────
    public async Task<bool> EnviarConfirmacionPedidoAsync(int pedidoId)
    {
        if (!await MailHabilitado("ecommerce", "enviarMailConfirmacion"))
        {
            _logger.LogWarning(
                "EnviarConfirmacionPedidoAsync: parámetro ecommerce/enviarMailConfirmacion no está en '1' — email no enviado para pedido #{Id}",
                pedidoId);
            return false;
        }

        var pedido = await _db.Pedidos
            .Include(p => p.Cliente)
            .Include(p => p.Detalles)
            .FirstOrDefaultAsync(p => p.Id == pedidoId);
        if (pedido is null)
        {
            _logger.LogWarning("EnviarConfirmacionPedidoAsync: pedido #{Id} no encontrado en BD", pedidoId);
            return false;
        }

        var emailDestino = ObtenerEmailCliente(pedido);
        if (string.IsNullOrWhiteSpace(emailDestino))
        {
            _logger.LogWarning(
                "EnviarConfirmacionPedidoAsync: pedido #{Id} sin email de cliente " +
                "(FkCliente={FkCliente}, Cliente.Email='{ClienteEmail}', EmailCliente='{EmailCliente}')",
                pedidoId,
                pedido.FkCliente,
                pedido.Cliente?.Email ?? "(null)",
                pedido.EmailCliente  ?? "(null)");
            return false;
        }

        _logger.LogInformation(
            "EnviarConfirmacionPedidoAsync: enviando confirmación pedido #{Id} → {Email}",
            pedidoId, emailDestino);

        var cobro = await _db.CobrosEcommerce
            .Where(c => c.FkPedido == pedidoId)
            .OrderByDescending(c => c.FechaCreacion)
            .FirstOrDefaultAsync();

        var nombreCliente = ObtenerNombreCliente(pedido);
        var empresa       = await _params.GetValorAsync("empresa", "nombre") ?? "LitoralMarket";

        // PDF adjunto si está habilitado
        byte[]? pdfBytes = null;
        if (cobro is not null && await MailHabilitado("ecommerce", "generarPdfPago"))
        {
            try { pdfBytes = await _pdf.GenerarComprobanteAsync(pedidoId, cobro.Id); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo generar el PDF para el pedido #{Id}", pedidoId);
            }
        }

        // Construir el mensaje
        var builder = new BodyBuilder();
        builder.HtmlBody = HtmlConfirmacionPedido(pedidoId, nombreCliente, empresa, pedido.Total ?? 0);

        if (pdfBytes is not null)
            builder.Attachments.Add($"comprobante-pedido-{pedidoId:D6}.pdf", pdfBytes,
                ContentType.Parse("application/pdf"));

        var asunto = $"Pedido #{pedidoId:D6} confirmado — {empresa}";
        return await EnviarAsync(emailDestino, asunto, builder);
    }

    // ──────────────────────────────────────────────────────────────
    // Link de pago MercadoPago (con QR embebido y PDF adjunto)
    // ──────────────────────────────────────────────────────────────
    public async Task<bool> EnviarLinkMercadoPagoAsync(int pedidoId, int cobroId)
    {
        if (!await MailHabilitado("ecommerce", "enviarMailMercadoPago"))
        {
            _logger.LogWarning(
                "EnviarLinkMercadoPagoAsync: parámetro ecommerce/enviarMailMercadoPago no está en '1' — email no enviado para pedido #{Id}",
                pedidoId);
            return false;
        }

        var pedido = await _db.Pedidos
            .Include(p => p.Cliente)
            .Include(p => p.Detalles)
            .FirstOrDefaultAsync(p => p.Id == pedidoId);
        if (pedido is null)
        {
            _logger.LogWarning("EnviarLinkMercadoPagoAsync: pedido #{Id} no encontrado en BD", pedidoId);
            return false;
        }

        var emailDestino = ObtenerEmailCliente(pedido);
        if (string.IsNullOrWhiteSpace(emailDestino))
        {
            _logger.LogWarning(
                "EnviarLinkMercadoPagoAsync: pedido #{Id} sin email de cliente " +
                "(FkCliente={FkCliente}, Cliente.Email='{ClienteEmail}', EmailCliente='{EmailCliente}')",
                pedidoId,
                pedido.FkCliente,
                pedido.Cliente?.Email ?? "(null)",
                pedido.EmailCliente  ?? "(null)");
            return false;
        }

        var cobro = await _db.CobrosEcommerce.FindAsync(cobroId);
        if (cobro is null)
        {
            _logger.LogWarning("EnviarLinkMercadoPagoAsync: cobro #{CobroId} no encontrado para pedido #{Id}", cobroId, pedidoId);
            return false;
        }

        _logger.LogInformation(
            "EnviarLinkMercadoPagoAsync: enviando link de pago pedido #{Id} → {Email}",
            pedidoId, emailDestino);

        var nombreCliente = ObtenerNombreCliente(pedido);
        var empresa       = await _params.GetValorAsync("empresa", "nombre") ?? "LitoralMarket";
        var linkPago      = cobro.MpLinkPago ?? string.Empty;

        // ── Descargar imagen del QR ──
        byte[]? qrBytes = null;
        if (!string.IsNullOrEmpty(linkPago))
        {
            try
            {
                var qrUrl = "https://api.qrserver.com/v1/create-qr-code/?size=280x280&margin=12&ecc=M&data="
                            + Uri.EscapeDataString(linkPago);
                var client = _http.CreateClient();
                qrBytes = await client.GetByteArrayAsync(qrUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo descargar el QR para el pedido #{Id}", pedidoId);
            }
        }

        // ── PDF adjunto ──
        byte[]? pdfBytes = null;
        if (await MailHabilitado("ecommerce", "generarPdfPago"))
        {
            try { pdfBytes = await _pdf.GenerarComprobanteAsync(pedidoId, cobroId); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo generar el PDF para el pedido #{Id}", pedidoId);
            }
        }

        // ── Construir el mensaje ──
        var builder  = new BodyBuilder();
        var qrCid    = "qr-mercadopago@litoralmarket";

        builder.HtmlBody = HtmlLinkMercadoPago(
            pedidoId, nombreCliente, empresa,
            linkPago, cobro.MpFechaExpiracion,
            pedido.Total ?? 0,
            qrBytes is not null ? qrCid : null);

        if (qrBytes is not null)
        {
            var qrImg = builder.LinkedResources.Add("qr.png", qrBytes, ContentType.Parse("image/png"));
            qrImg.ContentId = qrCid;
        }

        if (pdfBytes is not null)
            builder.Attachments.Add($"comprobante-pedido-{pedidoId:D6}.pdf", pdfBytes,
                ContentType.Parse("application/pdf"));

        var asunto = $"Tu link de pago — Pedido #{pedidoId:D6} — {empresa}";
        return await EnviarAsync(emailDestino, asunto, builder);
    }

    // ──────────────────────────────────────────────────────────────
    // Notificación interna al administrador
    // ──────────────────────────────────────────────────────────────
    public async Task EnviarNotificacionAdminAsync(int pedidoId)
    {
        if (!await MailHabilitado("ecommerce", "enviarMailAdmin")) return;

        var emailAdmin = await _params.GetValorAsync("mail", "emailAdmin");
        if (string.IsNullOrWhiteSpace(emailAdmin))
        {
            _logger.LogWarning("EnviarNotificacionAdminAsync: no hay mail/emailAdmin configurado");
            return;
        }

        var pedido = await _db.Pedidos
            .Include(p => p.Cliente)
            .Include(p => p.Detalles)
            .FirstOrDefaultAsync(p => p.Id == pedidoId);
        if (pedido is null) return;

        var empresa = await _params.GetValorAsync("empresa", "nombre") ?? "LitoralMarket";
        var nombre  = ObtenerNombreCliente(pedido);

        var builder = new BodyBuilder();
        builder.HtmlBody = HtmlNotificacionAdmin(pedidoId, nombre, empresa, pedido.Total ?? 0,
            pedido.EstadoEcommerce ?? "");

        var asunto = $"🛒 Nuevo pedido #{pedidoId:D6} — {nombre}";
        await EnviarAsync(emailAdmin, asunto, builder);
    }

    // ──────────────────────────────────────────────────────────────
    // Envío SMTP real con MailKit
    // ──────────────────────────────────────────────────────────────
    private async Task<bool> EnviarAsync(string emailDestino, string asunto, BodyBuilder builder)
    {
        var host            = await _params.GetValorAsync("mail", "host");
        var portStr         = await _params.GetValorAsync("mail", "port");
        var useSsl          = await _params.GetValorAsync("mail", "ssl") == "1";
        var usuario         = await _params.GetValorAsync("mail", "usuario");
        var password        = await _params.GetValorAsync("mail", "password");
        var remitente       = await _params.GetValorAsync("mail", "remitente");
        var nombreRemitente = await _params.GetValorAsync("mail", "nombreRemitente") ?? "LitoralMarket";

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(remitente))
        {
            _logger.LogWarning("EmailService: SMTP no configurado (mail/host o mail/remitente vacíos). " +
                               "Asunto: {Asunto} → {Destino}", asunto, emailDestino);
            return false;
        }

        if (!int.TryParse(portStr, out var port)) port = 587;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(nombreRemitente, remitente));
        message.To.Add(MailboxAddress.Parse(emailDestino));
        message.Subject = asunto;
        message.Body    = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        try
        {
            var socketOptions = useSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await smtp.ConnectAsync(host, port, socketOptions);

            if (!string.IsNullOrWhiteSpace(usuario) && !string.IsNullOrWhiteSpace(password))
                await smtp.AuthenticateAsync(usuario, password);

            await smtp.SendAsync(message);
            await smtp.DisconnectAsync(true);

            _logger.LogInformation("Email enviado: {Asunto} → {Destino}", asunto, emailDestino);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al enviar email '{Asunto}' a {Destino}", asunto, emailDestino);
            return false;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers internos
    // ──────────────────────────────────────────────────────────────
    private static string? ObtenerEmailCliente(Domain.Entities.Pedido pedido) =>
        pedido.FkCliente.HasValue ? pedido.Cliente?.Email : pedido.EmailCliente;

    private static string ObtenerNombreCliente(Domain.Entities.Pedido pedido) =>
        (pedido.FkCliente.HasValue
            ? pedido.Cliente?.NombreComercial
            : pedido.NombreCliente)
        ?? "Cliente";

    private async Task<bool> MailHabilitado(string modulo, string parametro) =>
        await _params.GetValorAsync(modulo, parametro) == "1";

    // ──────────────────────────────────────────────────────────────
    // Plantillas HTML
    // ──────────────────────────────────────────────────────────────
    private static string HtmlBase(string titulo, string empresa, string cuerpo) => $"""
        <!DOCTYPE html>
        <html lang="es">
        <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{titulo}</title></head>
        <body style="margin:0;padding:0;background:#f4f6f8;font-family:Arial,Helvetica,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f8;padding:30px 0;">
            <tr><td align="center">
              <table width="600" cellpadding="0" cellspacing="0"
                     style="background:#ffffff;border-radius:8px;overflow:hidden;max-width:600px;">

                <!-- Header -->
                <tr>
                  <td style="background:#1DB862;padding:24px 32px;">
                    <h1 style="margin:0;color:#ffffff;font-size:22px;font-weight:700;">{empresa}</h1>
                  </td>
                </tr>

                <!-- Cuerpo -->
                <tr>
                  <td style="padding:32px;">
                    {cuerpo}
                  </td>
                </tr>

                <!-- Footer -->
                <tr>
                  <td style="background:#f4f6f8;padding:16px 32px;border-top:1px solid #e0e0e0;">
                    <p style="margin:0;font-size:11px;color:#999;text-align:center;">
                      Este mensaje fue generado automáticamente por {empresa}.
                      Por favor no respondas a este correo.
                    </p>
                  </td>
                </tr>

              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    private static string HtmlLinkMercadoPago(
        int pedidoId, string nombreCliente, string empresa,
        string linkPago, DateTime? fechaExpiracion,
        decimal total, string? qrCid)
    {
        var vencimiento = fechaExpiracion.HasValue
            ? $"<p style='margin:0 0 16px;color:#666;font-size:14px;'>" +
              $"⏰ El link vence el <strong>{fechaExpiracion:dd/MM/yyyy 'a las' HH:mm} hs.</strong></p>"
            : string.Empty;

        var qrHtml = qrCid is not null
            ? $"""
              <div style="text-align:center;margin:24px 0 8px;">
                <img src="cid:{qrCid}" width="200" height="200" alt="QR MercadoPago"
                     style="border:1px solid #e0e0e0;border-radius:8px;padding:8px;" />
                <p style="margin:6px 0 0;font-size:12px;color:#999;">
                  Escaneá el QR con tu celular para pagar
                </p>
              </div>
              """
            : string.Empty;

        var cuerpo = $"""
            <h2 style="margin:0 0 8px;color:#1DB862;font-size:20px;">¡Tu pedido está reservado!</h2>
            <p style="margin:0 0 20px;color:#444;font-size:15px;">
              Hola <strong>{nombreCliente}</strong>, recibimos tu pedido
              <strong>#{pedidoId:D6}</strong> por un total de
              <strong style="color:#1DB862;">$ {total:N2}</strong>.
            </p>
            <p style="margin:0 0 12px;color:#444;font-size:14px;">
              Para confirmar tu compra, completá el pago haciendo clic en el botón:
            </p>

            <!-- Botón principal -->
            <div style="text-align:center;margin:20px 0;">
              <a href="{linkPago}"
                 style="display:inline-block;background:#009EE3;color:#ffffff;font-weight:700;
                        font-size:16px;padding:14px 36px;border-radius:8px;text-decoration:none;">
                Pagar con MercadoPago →
              </a>
            </div>

            {vencimiento}

            <!-- Separador o/otro -->
            <p style="text-align:center;color:#aaa;font-size:13px;margin:4px 0 0;">o escaneá el código QR</p>
            {qrHtml}

            <!-- Aviso -->
            <div style="background:#fff8dc;border:1px solid #c8a000;border-radius:6px;
                        padding:14px 16px;margin:24px 0 0;">
              <p style="margin:0;font-size:13px;color:#7a5000;">
                ⚠️ <strong>Aviso:</strong> Este comprobante no es válido como factura.
                La compra se efectivizará únicamente al recibir el pago.
              </p>
            </div>

            <p style="margin:20px 0 0;font-size:13px;color:#888;">
              Si tenés algún problema con el link de pago, podés copiarlo y pegarlo en tu navegador:<br/>
              <span style="color:#555;word-break:break-all;font-size:12px;">{linkPago}</span>
            </p>
            """;

        return HtmlBase($"Link de pago — Pedido #{pedidoId:D6}", empresa, cuerpo);
    }

    private static string HtmlConfirmacionPedido(
        int pedidoId, string nombreCliente, string empresa, decimal total)
    {
        var cuerpo = $"""
            <h2 style="margin:0 0 8px;color:#1DB862;font-size:20px;">¡Gracias por tu compra!</h2>
            <p style="margin:0 0 20px;color:#444;font-size:15px;">
              Hola <strong>{nombreCliente}</strong>, tu pedido
              <strong>#{pedidoId:D6}</strong> fue confirmado correctamente.
              Total: <strong style="color:#1DB862;">$ {total:N2}</strong>.
            </p>
            <p style="margin:0 0 16px;color:#444;font-size:14px;">
              Estamos preparando tu pedido. Te avisaremos cuando esté listo para la entrega.
            </p>
            <div style="background:#f0faf4;border:1px solid #1DB862;border-radius:6px;
                        padding:14px 16px;margin:20px 0 0;">
              <p style="margin:0;font-size:13px;color:#155724;">
                ✅ Tu pedido está en proceso. Podés contactarnos ante cualquier consulta.
              </p>
            </div>
            """;

        return HtmlBase($"Pedido #{pedidoId:D6} confirmado", empresa, cuerpo);
    }

    private static string HtmlNotificacionAdmin(
        int pedidoId, string nombreCliente, string empresa, decimal total, string estado)
    {
        var cuerpo = $"""
            <h2 style="margin:0 0 8px;color:#1DB862;font-size:18px;">Nuevo pedido recibido</h2>
            <table style="width:100%;border-collapse:collapse;margin-top:16px;font-size:14px;">
              <tr>
                <td style="padding:8px;background:#f4f6f8;font-weight:bold;width:40%;">N.° de pedido</td>
                <td style="padding:8px;">#{pedidoId:D6}</td>
              </tr>
              <tr>
                <td style="padding:8px;background:#f4f6f8;font-weight:bold;">Cliente</td>
                <td style="padding:8px;">{nombreCliente}</td>
              </tr>
              <tr>
                <td style="padding:8px;background:#f4f6f8;font-weight:bold;">Total</td>
                <td style="padding:8px;color:#1DB862;font-weight:bold;">$ {total:N2}</td>
              </tr>
              <tr>
                <td style="padding:8px;background:#f4f6f8;font-weight:bold;">Estado</td>
                <td style="padding:8px;">{estado}</td>
              </tr>
            </table>
            """;

        return HtmlBase($"Nuevo pedido #{pedidoId:D6} — {nombreCliente}", empresa, cuerpo);
    }
}
