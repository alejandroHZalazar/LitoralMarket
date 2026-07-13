using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LitoralMarket.Application.Helpers;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.IO;
using MimeKit.Text;

namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Servicio de email usando MailKit.
///
/// Precedencia de configuración SMTP (de mayor a menor prioridad):
///   1. Variables de entorno Railway (SMTP_HOST, SMTP_PORT, SMTP_SSL, SMTP_USERNAME,
///      SMTP_PASSWORD, SMTP_FROM, SMTP_FROM_NAME, SMTP_ADMIN_EMAIL)
///   2. Tabla `parametros` en BD (modulo='mail', parametro='host' | 'port' | ...)
///
/// Esto permite cambiar la config SMTP sin tocar la BD, solo con env vars en Railway.
/// </summary>
public class EmailService : IEmailService
{
    private readonly AppDbContext          _db;
    private readonly IParametrosService    _params;
    private readonly IPdfPagoService       _pdf;
    private readonly IHttpClientFactory    _http;
    private readonly ILogger<EmailService> _logger;
    private readonly IConfiguration        _config;

    public EmailService(
        AppDbContext          db,
        IParametrosService    params_,
        IPdfPagoService       pdf,
        IHttpClientFactory    http,
        ILogger<EmailService> logger,
        IConfiguration        config)
    {
        _db     = db;
        _params = params_;
        _pdf    = pdf;
        _http   = http;
        _logger = logger;
        _config = config;
    }

    // Env var tiene prioridad sobre valor de BD (permite override en Railway sin tocar la BD).
    // Lee desde IConfiguration y, si falla, directo del SO. Esto evita problemas si por
    // algún motivo IConfiguration en producción no está leyendo las env vars correctamente.
    private string? EnvOrParam(string envKey, string? dbValue)
    {
        var fromCfg = _config[envKey];
        if (!string.IsNullOrWhiteSpace(fromCfg)) return fromCfg;

        var fromEnv = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

        return dbValue;
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

        var nombreCliente  = ObtenerNombreCliente(pedido);
        var empresa        = await _params.GetValorAsync("empresa", "nombre") ?? "LitoralMarket";
        var telefonoWs     = await _params.GetValorAsync("empresa", "telefono");

        // PDF adjunto si está habilitado. Con cobro (flujo de pago) se genera el
        // comprobante con datos de pago; sin cobro (modo credenciales, pedido
        // directo) se genera el comprobante sin pago.
        byte[]? pdfBytes = null;
        if (await MailHabilitado("ecommerce", "generarPdfPago"))
        {
            try
            {
                pdfBytes = cobro is not null
                    ? await _pdf.GenerarComprobanteAsync(pedidoId, cobro.Id)
                    : await _pdf.GenerarComprobanteSinPagoAsync(pedidoId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo generar el PDF para el pedido #{Id}", pedidoId);
            }
        }

        // Construir el mensaje
        var builder = new BodyBuilder();
        builder.HtmlBody = HtmlConfirmacionPedido(pedidoId, nombreCliente, empresa, pedido.Total ?? 0, telefonoWs);

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
        var telefonoWs    = await _params.GetValorAsync("empresa", "telefono");
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
            qrBytes is not null ? qrCid : null,
            telefonoWs);

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

        // SMTP_ADMIN_EMAIL (env var) sobreescribe mail/emailAdmin (BD)
        var emailAdmin = EnvOrParam("SMTP_ADMIN_EMAIL", await _params.GetValorAsync("mail", "emailAdmin"));
        if (string.IsNullOrWhiteSpace(emailAdmin))
        {
            _logger.LogWarning(
                "EnviarNotificacionAdminAsync: no hay email de admin configurado. " +
                "Configurá la variable de entorno SMTP_ADMIN_EMAIL en Railway " +
                "o el parámetro mail/emailAdmin en la tabla parametros.");
            return;
        }

        var pedido = await _db.Pedidos
            .Include(p => p.Cliente)
            .Include(p => p.Detalles)
            .FirstOrDefaultAsync(p => p.Id == pedidoId);
        if (pedido is null) return;

        var empresa = await _params.GetValorAsync("empresa", "nombre") ?? "LitoralMarket";
        var nombre  = ObtenerNombreCliente(pedido);

        // ── Detectar productos del pedido que quedaron bajo el stock mínimo ──
        // La consulta se ejecuta DESPUÉS de que ConfirmarPagoAsync ya decrementó el stock,
        // por lo que los valores de StockProductos en BD ya reflejan el descuento.
        var fkProductos = pedido.Detalles
            .Where(d => d.FkProducto.HasValue)
            .Select(d => d.FkProducto!.Value)
            .Distinct()
            .ToList();

        var stockBajo = new List<StockBajoInfo>();
        if (fkProductos.Count > 0)
        {
            stockBajo = await _db.StockProductos
                .Where(s => s.FkProducto.HasValue
                         && fkProductos.Contains(s.FkProducto!.Value)
                         && s.CantidadMinima.HasValue
                         && s.CantidadMinima.Value > 0
                         && (s.Cantidad ?? 0) < s.CantidadMinima.Value)
                .Select(s => new StockBajoInfo(
                    s.Producto != null ? s.Producto.CodProveedor  : null,
                    s.Producto != null ? s.Producto.Descripcion   : null,
                    s.Cantidad       ?? 0,
                    s.CantidadMinima!.Value))
                .ToListAsync();

            if (stockBajo.Count > 0)
                _logger.LogInformation(
                    "EnviarNotificacionAdminAsync: pedido #{Id} — {N} producto(s) bajo stock mínimo",
                    pedidoId, stockBajo.Count);
        }

        var builder = new BodyBuilder();
        builder.HtmlBody = HtmlNotificacionAdmin(pedidoId, nombre, empresa, pedido.Total ?? 0,
            pedido.EstadoEcommerce ?? "", stockBajo);

        var asunto = $"🛒 Nuevo pedido #{pedidoId:D6} — {nombre}";
        await EnviarAsync(emailAdmin, asunto, builder);
    }

    // ──────────────────────────────────────────────────────────────
    // Envío de email — elige proveedor automáticamente:
    //
    //   Si está definida RESEND_API_KEY → usa Resend HTTP API (recomendado en
    //   Railway/Render/Fly que bloquean SMTP outbound).
    //
    //   Si no → cae al envío SMTP clásico vía MailKit (puede fallar con
    //   TimeoutException en hostings que bloqueen puertos 25/465/587).
    //
    // Variables de entorno (sobreescriben tabla parametros):
    //   RESEND_API_KEY   → si está, usa Resend (HTTPS); ignora todo lo SMTP
    //   SMTP_FROM        → dirección remitente (obligatorio en ambos proveedores)
    //   SMTP_FROM_NAME   → nombre visible del remitente
    //   SMTP_HOST/PORT/SSL/USERNAME/PASSWORD → solo para fallback SMTP
    //
    // Office365 → SMTP_HOST=smtp.office365.com  SMTP_PORT=587  SMTP_SSL=0
    // Gmail     → SMTP_HOST=smtp.gmail.com       SMTP_PORT=587  SMTP_SSL=0  (App Password)
    // Gmail SSL → SMTP_HOST=smtp.gmail.com       SMTP_PORT=465  SMTP_SSL=1
    // Resend    → RESEND_API_KEY=re_xxx  SMTP_FROM=tu@dominio-verificado.com
    // ──────────────────────────────────────────────────────────────
    private async Task<bool> EnviarAsync(string emailDestino, string asunto, BodyBuilder builder)
    {
        // ── 0. DIAGNÓSTICO de configuración al inicio (visible en Railway Logs) ──
        // Leemos las env vars desde DOS fuentes para detectar problemas de IConfiguration:
        //   • _config[]  → ASP.NET IConfiguration (debería incluir env vars por default)
        //   • Environment.GetEnvironmentVariable() → lectura directa del SO (sin transformaciones)
        // Si ambos son null/empty, la env var realmente no está seteada en Railway.
        // Si _config[] es null pero Environment.GetEnvironmentVariable() no, es un bug de config.
        var resendKeyCfg = _config["RESEND_API_KEY"];
        var resendKeyEnv = Environment.GetEnvironmentVariable("RESEND_API_KEY");
        var resendKey    = !string.IsNullOrWhiteSpace(resendKeyCfg) ? resendKeyCfg : resendKeyEnv;

        var smtpHostCfg = _config["SMTP_HOST"];
        var smtpHostEnv = Environment.GetEnvironmentVariable("SMTP_HOST");

        _logger.LogInformation(
            "EmailService: provider check → " +
            "RESEND_API_KEY: cfg={CfgLen} env={EnvLen} | " +
            "SMTP_HOST: cfg='{SmtpCfg}' env='{SmtpEnv}'",
            resendKeyCfg?.Length.ToString() ?? "null",
            resendKeyEnv?.Length.ToString() ?? "null",
            smtpHostCfg ?? "(null)",
            smtpHostEnv ?? "(null)");

        // ── 1. From/From-Name: necesarios para AMBOS proveedores ──────────
        var remitenteCfg    = _config["SMTP_FROM"];
        var remitenteEnv    = Environment.GetEnvironmentVariable("SMTP_FROM");
        var remitenteFromDb = await _params.GetValorAsync("mail", "remitente");
        var remitente       = !string.IsNullOrWhiteSpace(remitenteCfg) ? remitenteCfg
                            : !string.IsNullOrWhiteSpace(remitenteEnv) ? remitenteEnv
                            : remitenteFromDb;

        var nombreCfg       = _config["SMTP_FROM_NAME"];
        var nombreEnv       = Environment.GetEnvironmentVariable("SMTP_FROM_NAME");
        var nombreFromDb    = await _params.GetValorAsync("mail", "nombreRemitente");
        var nombreRemitente = !string.IsNullOrWhiteSpace(nombreCfg) ? nombreCfg
                            : !string.IsNullOrWhiteSpace(nombreEnv) ? nombreEnv
                            : nombreFromDb
                            ?? "LitoralMarket";

        if (string.IsNullOrWhiteSpace(remitente))
        {
            _logger.LogWarning(
                "EmailService: falta SMTP_FROM (dirección remitente). " +
                "Configurala como env var en Railway o en parametros (mail/remitente). " +
                "Asunto: {Asunto} → {Destino}",
                asunto, emailDestino);
            return false;
        }

        // ── 2. PROVIDER PRIORITY: Resend HTTP > SMTP ──────────────────────
        if (!string.IsNullOrWhiteSpace(resendKey))
        {
            _logger.LogInformation(
                "EmailService: → USANDO RESEND (HTTP API) from='{From}' to='{To}'",
                remitente, emailDestino);
            return await EnviarPorResendAsync(
                emailDestino, asunto, builder, resendKey, remitente, nombreRemitente);
        }

        _logger.LogWarning(
            "EmailService: ⚠️ RESEND_API_KEY no detectada (ni en IConfiguration ni en env directa). " +
            "Cayendo a SMTP — esto va a fallar en Railway porque bloquea SMTP outbound. " +
            "Verificá en Railway → Variables que RESEND_API_KEY esté definida " +
            "Y que hiciste Redeploy DESPUÉS de agregarla.");

        // ── Fallback: SMTP clásico ────────────────────────────────────────
        var host            = EnvOrParam("SMTP_HOST",      await _params.GetValorAsync("mail", "host"));
        var portStr         = EnvOrParam("SMTP_PORT",      await _params.GetValorAsync("mail", "port"));
        var sslStr          = EnvOrParam("SMTP_SSL",       await _params.GetValorAsync("mail", "ssl"));
        var usuario         = EnvOrParam("SMTP_USERNAME",  await _params.GetValorAsync("mail", "usuario"));
        var password        = EnvOrParam("SMTP_PASSWORD",  await _params.GetValorAsync("mail", "password"));

        bool viaEnv = _config["SMTP_HOST"] is { Length: > 0 };

        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning(
                "EmailService: SMTP no configurado y RESEND_API_KEY ausente. " +
                "Para Railway: agregá RESEND_API_KEY (recomendado) o SMTP_HOST. " +
                "Asunto: {Asunto} → {Destino}",
                asunto, emailDestino);
            return false;
        }

        if (!int.TryParse(portStr, out var port)) port = 587;
        var useSsl = sslStr == "1" || sslStr?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        // SecureSocketOptions correcto según puerto:
        //   465 → SslOnConnect (SSL inmediato)
        //   587 → StartTls    (STARTTLS obligatorio — falla si el server no lo soporta)
        //   25  → StartTlsWhenAvailable (best-effort, para servidores relay internos)
        var socketOptions = (port == 465 || useSsl)
            ? SecureSocketOptions.SslOnConnect
            : port == 587
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.StartTlsWhenAvailable;

        // Log de diagnóstico visible en Railway Logs
        _logger.LogInformation(
            "EmailService: [{Fuente}] SMTP host={Host}:{Port} tls={Tls} usuario={Usuario} → {Destino}",
            viaEnv ? "ENV" : "BD",
            host, port, socketOptions, usuario ?? "(sin auth)", emailDestino);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(nombreRemitente, remitente));
        message.To.Add(MailboxAddress.Parse(emailDestino));
        message.Subject = asunto;
        message.Body    = builder.ToMessageBody();

        // Timeout de operaciones SMTP (read/write socket). Default de MailKit es 100s.
        // Lo bajamos para fallar rápido en caso de bloqueos de red (Railway, etc.).
        using var smtp = new SmtpClient { Timeout = 30_000 };

        // Timeouts cortos por fase, vía CancellationToken — permiten distinguir
        // "Railway bloquea SMTP" (timeout en CONNECT, ~15 s) de "credenciales mal"
        // (rechazo rápido en AUTH).
        var fase = "init";
        try
        {
            // ── FASE 1: CONNECT ────────────────────────────────────────────
            fase = "connect";
            _logger.LogInformation(
                "EmailService: fase=CONNECT host={Host}:{Port} tls={Tls}",
                host, port, socketOptions);

            using (var ctsConnect = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                await smtp.ConnectAsync(host, port, socketOptions, ctsConnect.Token);
            }
            _logger.LogInformation("EmailService: fase=CONNECT OK");

            // ── FASE 2: AUTHENTICATE ──────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(usuario) && !string.IsNullOrWhiteSpace(password))
            {
                fase = "authenticate";
                _logger.LogInformation("EmailService: fase=AUTH usuario={Usuario}", usuario);

                using var ctsAuth = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await smtp.AuthenticateAsync(usuario, password, ctsAuth.Token);

                _logger.LogInformation("EmailService: fase=AUTH OK");
            }

            // ── FASE 3: SEND ──────────────────────────────────────────────
            fase = "send";
            _logger.LogInformation("EmailService: fase=SEND → {Destino}", emailDestino);

            using (var ctsSend = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                await smtp.SendAsync(message, ctsSend.Token);
            }

            // ── FASE 4: DISCONNECT ─────────────────────────────────────────
            fase = "disconnect";
            await smtp.DisconnectAsync(true);

            _logger.LogInformation(
                "EmailService: ✓ enviado — '{Asunto}' → {Destino}", asunto, emailDestino);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Timeout específico por fase — el más revelador es CONNECT
            if (fase == "connect")
            {
                _logger.LogError(
                    "EmailService: TIMEOUT en fase=CONNECT host={Host}:{Port}. " +
                    "Causa MÁS PROBABLE: tu hosting (Railway/Render/Fly) bloquea SMTP outbound. " +
                    "Soluciones: (1) cambiar a Resend/SendGrid/Mailgun (HTTPS, no SMTP) — recomendado; " +
                    "(2) probar puerto 465 con SMTP_SSL=1; " +
                    "(3) validar localmente con `telnet {Host} {Port}`.",
                    host, port, host, port);
            }
            else
            {
                _logger.LogError(
                    "EmailService: TIMEOUT en fase={Fase} host={Host}:{Port}", fase, host, port);
            }
            try { await smtp.DisconnectAsync(false); } catch { }
            return false;
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            // 535 / 534 → credenciales inválidas o seguridad de Gmail bloqueando
            _logger.LogError(
                "EmailService: AUTH FALLÓ en fase={Fase} usuario={Usuario} — {Msg}. " +
                "Si es Gmail: tiene que ser App Password (16 chars, 2FA activado), NO la contraseña normal. " +
                "Si es Office365: la cuenta debe permitir SMTP AUTH (a veces desactivado por política org).",
                fase, usuario, ex.Message);
            try { await smtp.DisconnectAsync(false); } catch { }
            return false;
        }
        catch (MailKit.Net.Smtp.SmtpCommandException ex)
        {
            // Error de protocolo SMTP: el servidor rechazó el comando
            // StatusCode y Response dan la causa exacta (ej: 535 = auth failed, 550 = relay denied)
            _logger.LogError(
                "EmailService: SMTP rechazó comando en fase={Fase} — " +
                "host={Host}:{Port} errorCode={Code} statusCode={Status} — {SmtpMsg} — asunto='{Asunto}'",
                fase, host, port, (int)ex.StatusCode, ex.StatusCode, ex.Message, asunto);
            try { await smtp.DisconnectAsync(false); } catch { }
            return false;
        }
        catch (MailKit.Net.Smtp.SmtpProtocolException ex)
        {
            _logger.LogError(
                "EmailService: error de protocolo SMTP/TLS en fase={Fase} — " +
                "host={Host}:{Port} — {Msg}",
                fase, host, port, ex.Message);
            try { await smtp.DisconnectAsync(false); } catch { }
            return false;
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // Errores de socket: red caída, DNS, conexión rechazada
            _logger.LogError(
                "EmailService: error de SOCKET en fase={Fase} host={Host}:{Port} — " +
                "errorCode={Code} ({Name}) — {Msg}. " +
                "Si SocketErrorCode=ConnectionRefused o NetworkUnreachable, " +
                "tu hosting bloquea el puerto SMTP.",
                fase, host, port, ex.ErrorCode, ex.SocketErrorCode, ex.Message);
            try { await smtp.DisconnectAsync(false); } catch { }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EmailService: excepción inesperada en fase={Fase} — " +
                "host={Host}:{Port} tipo={Tipo} — asunto='{Asunto}' → {Destino}",
                fase, host, port, ex.GetType().Name, asunto, emailDestino);
            try { await smtp.DisconnectAsync(false); } catch { }
            return false;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Envío vía Resend HTTP API
    //
    // Endpoint: POST https://api.resend.com/emails
    // Auth: Authorization: Bearer {RESEND_API_KEY}
    //
    // Ventaja sobre SMTP: usa HTTPS (puerto 443) → no se ve afectado por
    // bloqueos de SMTP outbound en Railway/Render/Fly/Heroku.
    //
    // Requisitos:
    //   • RESEND_API_KEY env var (obtenida en https://resend.com/api-keys)
    //   • SMTP_FROM debe estar verificado en Resend:
    //       - dominio propio verificado (DNS DKIM/SPF) → cualquier @tu-dominio.com
    //       - sin dominio verificado → solo onboarding@resend.dev y solo enviás
    //         al email registrado en tu cuenta Resend (modo test)
    // ──────────────────────────────────────────────────────────────
    private async Task<bool> EnviarPorResendAsync(
        string emailDestino,
        string asunto,
        BodyBuilder builder,
        string resendKey,
        string remitente,
        string nombreRemitente)
    {
        _logger.LogInformation(
            "EmailService: [RESEND] enviando vía HTTP API — '{Asunto}' → {Destino} (from={From})",
            asunto, emailDestino, remitente);

        try
        {
            // ── Construir lista de attachments + linked resources (inline) ──
            var attachments = new List<object>();

            foreach (var resource in builder.LinkedResources)
            {
                var item = await ConvertirMimeEntityParaResendAsync(resource, esInline: true);
                if (item is not null) attachments.Add(item);
            }
            foreach (var attach in builder.Attachments)
            {
                var item = await ConvertirMimeEntityParaResendAsync(attach, esInline: false);
                if (item is not null) attachments.Add(item);
            }

            // ── Formato From: "Nombre <email@dominio>" si hay nombre ──
            var fromHeader = !string.IsNullOrWhiteSpace(nombreRemitente)
                ? $"{nombreRemitente} <{remitente}>"
                : remitente;

            // ── Payload Resend ──
            var payload = new ResendPayload
            {
                From        = fromHeader,
                To          = new[] { emailDestino },
                Subject     = asunto,
                Html        = builder.HtmlBody ?? string.Empty,
                Attachments = attachments.Count > 0 ? attachments : null
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower
            });

            // ── POST con timeout controlado de 30 s ──
            var http = _http.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", resendKey);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var cts     = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            HttpResponseMessage resp;
            try
            {
                resp = await http.PostAsync("https://api.resend.com/emails", content, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogError(
                    "EmailService: [RESEND] TIMEOUT al POST a api.resend.com — " +
                    "verificá conectividad HTTPS desde Railway y validez de RESEND_API_KEY");
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "EmailService: [RESEND] error de red al POST — {Msg}", ex.Message);
                return false;
            }

            var respBody = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                // Códigos comunes: 401 (API key inválida), 403 (dominio no verificado),
                // 422 (payload inválido), 429 (rate limit)
                _logger.LogError(
                    "EmailService: [RESEND] HTTP {Code} — body={Body} — asunto='{Asunto}' → {Destino}. " +
                    "401=API key inválida; 403=remitente no verificado en Resend; " +
                    "422=payload inválido; 429=rate limit.",
                    (int)resp.StatusCode, respBody, asunto, emailDestino);
                return false;
            }

            _logger.LogInformation(
                "EmailService: [RESEND] ✓ enviado — '{Asunto}' → {Destino} — resp={Body}",
                asunto, emailDestino, respBody);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EmailService: [RESEND] excepción inesperada {Tipo} — '{Asunto}' → {Destino}",
                ex.GetType().Name, asunto, emailDestino);
            return false;
        }
    }

    /// <summary>
    /// Convierte un MimeEntity (attachment o linked resource) al formato JSON
    /// que espera la API de Resend: { filename, content (base64), content_id?, content_type }
    /// </summary>
    private static async Task<object?> ConvertirMimeEntityParaResendAsync(
        MimeEntity entity, bool esInline)
    {
        if (entity is not MimePart part || part.Content is null) return null;

        // Decodificar el contenido a bytes raw (sin codificación MIME)
        using var ms = new MemoryStream();
        await part.Content.DecodeToAsync(ms);
        var bytes = ms.ToArray();
        if (bytes.Length == 0) return null;

        var b64 = Convert.ToBase64String(bytes);

        if (esInline)
        {
            // ContentId puede venir entre <>, Resend lo quiere sin
            var cid = part.ContentId?.Trim('<', '>') ?? string.Empty;
            return new
            {
                filename     = part.FileName ?? "inline",
                content      = b64,
                content_id   = cid,
                content_type = part.ContentType.MimeType
            };
        }

        return new
        {
            filename     = part.FileName ?? "attachment",
            content      = b64,
            content_type = part.ContentType.MimeType
        };
    }

    /// <summary>
    /// Producto del pedido cuyo stock quedó por debajo del mínimo configurado
    /// luego de descontar las unidades vendidas.
    /// </summary>
    private record StockBajoInfo(
        string? CodProveedor,
        string? Descripcion,
        decimal StockActual,
        decimal StockMinimo);

    /// <summary>DTO interno para serializar el payload a Resend con snake_case.</summary>
    private sealed class ResendPayload
    {
        public string From    { get; set; } = string.Empty;
        public string[] To    { get; set; } = Array.Empty<string>();
        public string Subject { get; set; } = string.Empty;
        public string Html    { get; set; } = string.Empty;
        public List<object>? Attachments { get; set; }
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
        decimal total, string? qrCid,
        string? telefonoWhatsApp = null)
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

        // ── Sección de coordinación WhatsApp (solo si hay teléfono configurado) ──
        var seccionWhatsAppMp = string.Empty;
        if (!string.IsNullOrWhiteSpace(telefonoWhatsApp))
        {
            var waNumeroMp = PhoneHelper.NormalizarParaWhatsApp(telefonoWhatsApp);
            var waLinkMp   = string.IsNullOrEmpty(waNumeroMp)
                ? "#"
                : $"https://wa.me/{waNumeroMp}";

            seccionWhatsAppMp =
                "<div style='background:#f0fdf4;border:1px solid #25D366;border-radius:6px;" +
                "padding:16px;margin-top:20px;'>" +
                "<p style='margin:0 0 8px;font-size:14px;color:#155724;font-weight:700;'>" +
                "📦 Coordinación de entrega</p>" +
                "<p style='margin:0 0 12px;font-size:13px;color:#444;'>" +
                "Una vez confirmado el pago, comunicate por WhatsApp para coordinar la entrega:</p>" +
                $"<a href='{waLinkMp}' target='_blank' rel='noopener noreferrer' " +
                "style='display:inline-block;background:#25D366;color:#ffffff;font-weight:700;" +
                "font-size:14px;padding:10px 22px;border-radius:6px;text-decoration:none;'>" +
                $"📱 WhatsApp {System.Net.WebUtility.HtmlEncode(telefonoWhatsApp)}</a>" +
                "<p style='margin:10px 0 0;font-size:12px;color:#666;'>" +
                "Nuestro equipo te ayudará a definir el horario de entrega.</p>" +
                "</div>";
        }

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

            {seccionWhatsAppMp}

            <p style="margin:20px 0 0;font-size:13px;color:#888;">
              Si tenés algún problema con el link de pago, podés copiarlo y pegarlo en tu navegador:<br/>
              <span style="color:#555;word-break:break-all;font-size:12px;">{linkPago}</span>
            </p>
            """;

        return HtmlBase($"Link de pago — Pedido #{pedidoId:D6}", empresa, cuerpo);
    }

    private static string HtmlConfirmacionPedido(
        int pedidoId, string nombreCliente, string empresa, decimal total,
        string? telefonoWhatsApp = null)
    {
        // ── Sección de coordinación WhatsApp (solo si hay teléfono configurado) ──
        var seccionWhatsApp = string.Empty;
        if (!string.IsNullOrWhiteSpace(telefonoWhatsApp))
        {
            var waNumero = PhoneHelper.NormalizarParaWhatsApp(telefonoWhatsApp);
            var waLink   = string.IsNullOrEmpty(waNumero)
                ? "#"
                : $"https://wa.me/{waNumero}";

            seccionWhatsApp =
                "<div style='background:#f0fdf4;border:1px solid #25D366;border-radius:6px;" +
                "padding:16px;margin-top:20px;'>" +
                "<p style='margin:0 0 8px;font-size:14px;color:#155724;font-weight:700;'>" +
                "📦 Coordinación de entrega</p>" +
                "<p style='margin:0 0 12px;font-size:13px;color:#444;'>" +
                "Para coordinar el horario de entrega de tu pedido, comunicate por WhatsApp:</p>" +
                $"<a href='{waLink}' target='_blank' rel='noopener noreferrer' " +
                "style='display:inline-block;background:#25D366;color:#ffffff;font-weight:700;" +
                "font-size:14px;padding:10px 22px;border-radius:6px;text-decoration:none;'>" +
                $"📱 WhatsApp {System.Net.WebUtility.HtmlEncode(telefonoWhatsApp)}</a>" +
                "<p style='margin:10px 0 0;font-size:12px;color:#666;'>" +
                "Nuestro equipo te ayudará a definir el horario de entrega.</p>" +
                "</div>";
        }

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
            {seccionWhatsApp}
            """;

        return HtmlBase($"Pedido #{pedidoId:D6} confirmado", empresa, cuerpo);
    }

    private static string HtmlNotificacionAdmin(
        int pedidoId, string nombreCliente, string empresa, decimal total, string estado,
        List<StockBajoInfo> stockBajo)
    {
        // ── Sección de stock bajo (solo si hay productos afectados) ──────────
        var seccionStockBajo = string.Empty;
        if (stockBajo.Count > 0)
        {
            var filas = string.Concat(stockBajo.Select(s =>
            {
                var desc = System.Net.WebUtility.HtmlEncode(s.Descripcion ?? "Sin descripción");
                var cod  = string.IsNullOrWhiteSpace(s.CodProveedor)
                               ? "—"
                               : System.Net.WebUtility.HtmlEncode(s.CodProveedor);

                return
                    "<tr>" +
                    $"<td style='padding:7px 10px;border-bottom:1px solid #ffe082;font-size:13px;'>" +
                    $"<span style='color:#856404;font-size:11px;font-weight:600;'>[{cod}]</span>" +
                    $"&nbsp;{desc}</td>" +
                    $"<td style='padding:7px 10px;border-bottom:1px solid #ffe082;text-align:center;" +
                    $"font-size:13px;font-weight:700;color:#c0392b;'>{s.StockActual:N0}</td>" +
                    $"<td style='padding:7px 10px;border-bottom:1px solid #ffe082;text-align:center;" +
                    $"font-size:13px;color:#6d4c00;'>{s.StockMinimo:N0}</td>" +
                    "</tr>";
            }));

            seccionStockBajo =
                "<div style='background:#fff8e1;border:1px solid #ffc107;border-radius:6px;" +
                "padding:16px;margin-top:24px;'>" +
                "<p style='margin:0 0 12px;font-size:14px;color:#856404;font-weight:700;'>" +
                $"⚠️ {stockBajo.Count} producto{(stockBajo.Count == 1 ? "" : "s")} " +
                "por debajo del stock mínimo</p>" +
                "<table style='width:100%;border-collapse:collapse;'>" +
                "<thead><tr style='background:rgba(255,193,7,.15);'>" +
                "<th style='padding:6px 10px;text-align:left;font-size:12px;color:#856404;" +
                "font-weight:600;border-bottom:2px solid #ffc107;'>Producto</th>" +
                "<th style='padding:6px 10px;text-align:center;font-size:12px;color:#856404;" +
                "font-weight:600;border-bottom:2px solid #ffc107;width:90px;'>Stock actual</th>" +
                "<th style='padding:6px 10px;text-align:center;font-size:12px;color:#856404;" +
                "font-weight:600;border-bottom:2px solid #ffc107;width:90px;'>Mínimo</th>" +
                "</tr></thead>" +
                $"<tbody>{filas}</tbody>" +
                "</table></div>";
        }

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
            {seccionStockBajo}
            """;

        return HtmlBase($"Nuevo pedido #{pedidoId:D6} — {nombreCliente}", empresa, cuerpo);
    }
}
