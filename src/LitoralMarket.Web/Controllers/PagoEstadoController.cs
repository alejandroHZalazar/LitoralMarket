using System.Net.Http.Headers;
using System.Text.Json;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LitoralMarket.Web.Controllers;

/// <summary>
/// Endpoint liviano para que el frontend consulte el estado del pago de un pedido
/// mientras el usuario permanece en la página de pago (sin webhook activo).
///
/// Flujo por ciclo de polling:
///   1. Consulta BD → si ya está confirmado, responde de inmediato (costo mínimo).
///   2. Si el cobro MP sigue pendiente, consulta la API de MP UNA VEZ cada 10 s
///      por pedido (rate-limit vía IMemoryCache).
///   3. Llama DOS endpoints de MP en paralelo:
///        a) /merchant_orders/search?preference_id=…   (rápido para web checkout)
///        b) /v1/payments/search?external_reference=…  (captura pagos QR/mobile)
///      El primero que detecte un pago aprobado define el estado.
///   4. Si encuentra el pago aprobado → llama ProcesarNotificacionMercadoPagoAsync
///      (idempotente) y responde "confirmado".
///
/// Impacto en API de MP: máximo 12 req/min por pedido activo (2 endpoints cada 10 s).
/// Sin riesgo de rate-limit porque cada usuario tiene 1 pedido activo a la vez.
/// </summary>
[ApiController]
[Route("api/pago-estado")]
public class PagoEstadoController : ControllerBase
{
    private readonly AppDbContext                  _db;
    private readonly IPagoEcommerceService         _pagos;
    private readonly IParametrosService            _params;
    private readonly IHttpClientFactory            _httpFactory;
    private readonly IMemoryCache                  _cache;
    private readonly ILogger<PagoEstadoController> _logger;

    /// Intervalo mínimo entre llamadas directas a la API de MP por pedido.
    /// merchant_orders es liviano y refresca en segundos, así que 10 s es seguro.
    private static readonly TimeSpan IntervaloMpCheck = TimeSpan.FromSeconds(10);

    public PagoEstadoController(
        AppDbContext                  db,
        IPagoEcommerceService         pagos,
        IParametrosService            params_,
        IHttpClientFactory            httpFactory,
        IMemoryCache                  cache,
        ILogger<PagoEstadoController> logger)
    {
        _db          = db;
        _pagos       = pagos;
        _params      = params_;
        _httpFactory = httpFactory;
        _cache       = cache;
        _logger      = logger;
    }

    // ─── GET /api/pago-estado/{pedidoId} ───────────────────────────────────
    [HttpGet("{pedidoId:int}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Get(int pedidoId, CancellationToken ct)
    {
        // ── 1. Estado actual en BD (consulta barata, sin ir a MP) ──────────
        var estadoPedido = await _db.Pedidos
            .Where(p => p.Id == pedidoId)
            .Select(p => p.EstadoEcommerce)
            .FirstOrDefaultAsync(ct);

        if (estadoPedido == "confirmado")
            return Json("confirmado");

        // Cobro MercadoPago más reciente para este pedido
        var cobro = await _db.CobrosEcommerce
            .Where(c => c.FkPedido == pedidoId && c.Tipo == "mercadopago")
            .OrderByDescending(c => c.FechaCreacion)
            .Select(c => new { c.Estado, c.MpPreferenceId })
            .FirstOrDefaultAsync(ct);

        if (cobro?.Estado == "aprobado")  return Json("confirmado");
        if (cobro?.Estado == "rechazado") return Json("rechazado");
        if (cobro?.Estado == "cancelado") return Json("cancelado");

        // Si no hay cobro MP o no tiene preferencia → sin datos suficientes
        if (cobro is null || string.IsNullOrEmpty(cobro.MpPreferenceId))
            return Json("pendiente");

        // ── 2. Rate-limit: solo consultar MP una vez cada 20 s por pedido ──
        var cacheKey = $"mp_poll_{pedidoId}";
        if (_cache.TryGetValue(cacheKey, out _))
            return Json("pendiente");   // demasiado pronto, evitar hammering

        _cache.Set(cacheKey, true, IntervaloMpCheck);

        // ── 3. Consultar API de MP por preference_id ───────────────────────
        var accessToken = await _params.GetValorAsync("mercadopago", "accessToken");
        if (string.IsNullOrWhiteSpace(accessToken))
            return Json("pendiente");

        // ── 3. Consultar MP con DOS estrategias en paralelo (catch-all) ─────
        //
        // Estrategia A — merchant_orders/search?preference_id=…
        //   Rápido para pagos hechos en el MISMO navegador vía web checkout.
        //   Lento (30-90 s) para pagos QR/mobile desde la app de MP.
        //
        // Estrategia B — payments/search?external_reference={pedidoId}
        //   external_reference (el pedidoId) está indexado independientemente
        //   del canal — captura pagos desde QR/celular/app/transferencia
        //   en pocos segundos, sin importar desde qué dispositivo se pagó.
        //
        // Ejecutamos ambas en paralelo y devolvemos confirmado en cuanto
        // cualquiera encuentre un pago aprobado.

        var http = _httpFactory.CreateClient("MercadoPago");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var tareaMerchantOrders = ConsultarMerchantOrdersAsync(http, cobro.MpPreferenceId, pedidoId, ct);
        var tareaPaymentsSearch = ConsultarPaymentsSearchAsync(http, pedidoId, ct);

        DeteccionPago resA, resB;
        try
        {
            await Task.WhenAll(tareaMerchantOrders, tareaPaymentsSearch);
            resA = tareaMerchantOrders.Result;
            resB = tareaPaymentsSearch.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PagoEstado: error en consultas paralelas a MP — pedidoId={Id}", pedidoId);
            return Json("pendiente");
        }

        // Priorizar el que detectó el pago (no importa cuál fue)
        var detectado = resA.Estado == "confirmado" ? resA
                      : resB.Estado == "confirmado" ? resB
                      : resA.Estado == "rechazado" || resB.Estado == "rechazado" ? new DeteccionPago("rechazado", string.Empty)
                      : resA.Estado == "cancelado" || resB.Estado == "cancelado" ? new DeteccionPago("cancelado", string.Empty)
                      : new DeteccionPago("pendiente", string.Empty);

        _logger.LogInformation(
            "PagoEstado: pedidoId={Id} merchant_orders={A} payments_search={B} → estado={Final}",
            pedidoId, resA.Estado, resB.Estado, detectado.Estado);

        if (detectado.Estado != "confirmado")
            return Json(detectado.Estado);

        // ── 4. Pago aprobado encontrado → procesar (idempotente) ──────────
        if (string.IsNullOrEmpty(detectado.PaymentId))
        {
            // Detectado pero sin paymentId (caso raro) → marcamos confirmado
            // y dejamos que el worker complete los detalles
            return Json("confirmado");
        }

        _logger.LogInformation(
            "PagoEstado: pago aprobado detectado → pedidoId={PedidoId} paymentId={PaymentId}",
            pedidoId, detectado.PaymentId);

        try
        {
            await _pagos.ProcesarNotificacionMercadoPagoAsync(detectado.PaymentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PagoEstado: error al procesar paymentId={Pid}", detectado.PaymentId);
            // Aún así devolvemos "confirmado" porque el pago existe en MP
        }

        return Json("confirmado");
    }

    // ─────────────────────────────────────────────────────────────────────────
    private record DeteccionPago(string Estado, string PaymentId);

    /// <summary>
    /// Consulta MP por merchant_orders del preference_id. Rápido para web checkout.
    /// </summary>
    private async Task<DeteccionPago> ConsultarMerchantOrdersAsync(
        HttpClient http, string preferenceId, int pedidoId, CancellationToken ct)
    {
        try
        {
            var url = "https://api.mercadopago.com/merchant_orders/search" +
                      $"?preference_id={Uri.EscapeDataString(preferenceId)}";

            var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("PagoEstado/MO: HTTP {Code} pedidoId={Id}",
                    (int)resp.StatusCode, pedidoId);
                return new DeteccionPago("pendiente", string.Empty);
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            if (!root.TryGetProperty("elements", out var elements)
                || elements.GetArrayLength() == 0)
                return new DeteccionPago("pendiente", string.Empty);

            // Tomar el merchant_order más reciente
            var order = elements[elements.GetArrayLength() - 1];

            if (order.TryGetProperty("payments", out var payments)
                && payments.ValueKind == JsonValueKind.Array)
            {
                return EvaluarArrayPayments(payments);
            }

            return new DeteccionPago("pendiente", string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PagoEstado/MO: excepción pedidoId={Id}", pedidoId);
            return new DeteccionPago("pendiente", string.Empty);
        }
    }

    /// <summary>
    /// Consulta MP por payments/search filtrando por external_reference (pedidoId).
    /// Captura pagos QR/mobile/app — independiente del canal de origen.
    /// </summary>
    private async Task<DeteccionPago> ConsultarPaymentsSearchAsync(
        HttpClient http, int pedidoId, CancellationToken ct)
    {
        try
        {
            var url = "https://api.mercadopago.com/v1/payments/search" +
                      $"?external_reference={pedidoId}" +
                      "&sort=date_created&criteria=desc&limit=5";

            var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("PagoEstado/PS: HTTP {Code} pedidoId={Id}",
                    (int)resp.StatusCode, pedidoId);
                return new DeteccionPago("pendiente", string.Empty);
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var results)
                || results.GetArrayLength() == 0)
                return new DeteccionPago("pendiente", string.Empty);

            return EvaluarArrayPayments(results);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PagoEstado/PS: excepción pedidoId={Id}", pedidoId);
            return new DeteccionPago("pendiente", string.Empty);
        }
    }

    /// <summary>
    /// Recorre un array de payments (ya sea de merchant_order.payments
    /// o payments/search.results) y determina el estado más relevante.
    /// </summary>
    private static DeteccionPago EvaluarArrayPayments(JsonElement payments)
    {
        var estado    = "pendiente";
        var paymentId = string.Empty;

        foreach (var p in payments.EnumerateArray())
        {
            var status = p.TryGetProperty("status", out var s) ? s.GetString() : null;
            var idProp = p.TryGetProperty("id", out var i) ? i : default;

            var idStr = idProp.ValueKind == JsonValueKind.Number
                ? idProp.GetInt64().ToString()
                : (idProp.ValueKind == JsonValueKind.String ? idProp.GetString() ?? "" : "");

            if (status == "approved")
            {
                // Aprobado tiene prioridad máxima — devolver inmediatamente
                return new DeteccionPago("confirmado", idStr);
            }
            if (status == "rejected" && estado == "pendiente")
            {
                estado    = "rechazado";
                paymentId = idStr;
            }
            else if (status == "cancelled" && estado == "pendiente")
            {
                estado    = "cancelado";
                paymentId = idStr;
            }
        }

        return new DeteccionPago(estado, paymentId);
    }

    // Helper: serializa un string como JSON sin crear un objeto anónimo
    private static JsonResult Json(string estado) =>
        new(new { estado }, new System.Text.Json.JsonSerializerOptions());
}
