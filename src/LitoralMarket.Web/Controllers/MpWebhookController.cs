using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LitoralMarket.Web.Controllers;

/// <summary>
/// Endpoint que recibe las notificaciones webhook de MercadoPago.
/// URL: /api/mp-webhook  (configurar como notification_url en la preferencia).
///
/// Compatible con:
///   - IPN v1: ?type=payment&amp;data.id=PAYMENT_ID  (query string)
///   - Webhook v2: POST con cuerpo JSON + cabecera x-signature
///
/// Si el parámetro mercadopago/webhookSecret está configurado, valida
/// la firma HMAC-SHA256 antes de procesar la notificación.
/// </summary>
[ApiController]
[Route("api/mp-webhook")]
public class MpWebhookController : ControllerBase
{
    private readonly IPagoEcommerceService   _pagos;
    private readonly IParametrosService      _params;
    private readonly ILogger<MpWebhookController> _logger;

    public MpWebhookController(
        IPagoEcommerceService        pagos,
        IParametrosService           params_,
        ILogger<MpWebhookController> logger)
    {
        _pagos   = pagos;
        _params  = params_;
        _logger  = logger;
    }

    // ─── GET: health-check ──────────────────────────────────────────
    [HttpGet]
    public IActionResult Get() => Ok("MP Webhook activo ✓");

    // ─── POST: notificación de pago ─────────────────────────────────
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Post(
        [FromQuery] string? type,
        [FromQuery(Name = "data.id")] string? dataId,
        [FromQuery] string? topic,
        [FromQuery] string? id)
    {
        try
        {
            // ── 1. Leer cuerpo de la request (para webhook v2) ──────
            string body = string.Empty;
            Request.EnableBuffering();
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
                body = await reader.ReadToEndAsync();
            Request.Body.Position = 0;

            _logger.LogInformation(
                "MP Webhook recibido — type={Type} dataId={DataId} topic={Topic} id={Id} bodyLen={Len}",
                type, dataId, topic, id, body.Length);

            // ── 2. Validar firma (OBLIGATORIO — P7-A) ──────────────────
            // Si mercadopago/webhookSecret no está configurado en BD, rechazamos el webhook.
            // Esto evita que cualquiera pueda confirmar pedidos enviando un POST arbitrario.
            var secret = await _params.GetValorAsync("mercadopago", "webhookSecret");
            if (string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogError(
                    "MP Webhook: parámetro 'mercadopago/webhookSecret' no configurado en BD. " +
                    "El webhook fue rechazado por seguridad. Configure el secret en la tabla parametros.");
                return StatusCode(500, "Webhook secret no configurado.");
            }

            if (!ValidarFirma(secret, dataId ?? id, Request.Headers))
            {
                _logger.LogWarning("MP Webhook: firma inválida — rechazada");
                return Ok(); // 200 para que MP no reintente indefinidamente
            }

            // ── 3. Determinar tipo de evento y paymentId ────────────
            var tipoEvento = type ?? topic ?? string.Empty;
            var paymentId  = dataId ?? id ?? string.Empty;

            // Webhook v2: el body JSON puede traer el ID si no viene en query
            if (string.IsNullOrEmpty(paymentId) && !string.IsNullOrEmpty(body))
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;

                    // { "type":"payment", "data": { "id": "123456" } }
                    if (root.TryGetProperty("type", out var typeProp))
                        tipoEvento = typeProp.GetString() ?? tipoEvento;

                    if (root.TryGetProperty("data", out var dataProp)
                        && dataProp.TryGetProperty("id", out var idProp))
                    {
                        paymentId = idProp.ValueKind == JsonValueKind.Number
                            ? idProp.GetInt64().ToString()
                            : idProp.GetString() ?? string.Empty;
                    }
                }
                catch (JsonException)
                {
                    // body no es JSON válido — ignorar y continuar con query params
                }
            }

            _logger.LogInformation(
                "MP Webhook: type={Type} paymentId={PaymentId}",
                tipoEvento, paymentId);

            // ── 4. Procesar si es una notificación de pago ──────────
            var esNotificacionPago =
                tipoEvento is "payment" or "payments" or "payment.created" or "payment.updated";

            if (esNotificacionPago && !string.IsNullOrEmpty(paymentId))
            {
                await _pagos.ProcesarNotificacionMercadoPagoAsync(paymentId);
            }
            else
            {
                _logger.LogDebug(
                    "MP Webhook: evento '{Type}' ignorado (no es pago o sin ID)", tipoEvento);
            }
        }
        catch (Exception ex)
        {
            // Nunca retornar 5xx: MP reintentaría indefinidamente
            _logger.LogError(ex, "Error procesando webhook MercadoPago");
        }

        return Ok();
    }

    // ─── Validación HMAC-SHA256 ─────────────────────────────────────
    /// <summary>
    /// Valida la cabecera x-signature que envía MercadoPago webhook v2.
    /// Formato: "ts=TIMESTAMP,v1=HASH"
    /// Hash = HMAC-SHA256(secret, "id:{dataId};request-id:{x-request-id};ts:{ts}")
    /// </summary>
    private static bool ValidarFirma(
        string secret,
        string? paymentId,
        IHeaderDictionary headers)
    {
        try
        {
            var xSignature = headers["x-signature"].FirstOrDefault()  ?? string.Empty;
            var xRequestId = headers["x-request-id"].FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrEmpty(xSignature)) return false;

            // Parsear "ts=1234,v1=abcdef..."
            string ts = string.Empty, v1 = string.Empty;
            foreach (var part in xSignature.Split(','))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                if (kv[0] == "ts") ts = kv[1];
                if (kv[0] == "v1") v1 = kv[1];
            }

            if (string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(v1)) return false;

            var manifest = $"id:{paymentId};request-id:{xRequestId};ts:{ts}";

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash    = hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest));
            var hashHex = Convert.ToHexString(hash).ToLowerInvariant();

            return hashHex == v1.ToLowerInvariant();
        }
        catch
        {
            return false;
        }
    }
}
