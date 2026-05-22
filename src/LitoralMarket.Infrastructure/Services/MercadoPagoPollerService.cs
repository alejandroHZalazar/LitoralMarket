using System.Net.Http.Headers;
using System.Text.Json;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Servicio de fondo que sondea la API de MercadoPago cada N minutos
/// buscando pagos aprobados y los procesa si aún no fueron registrados.
///
/// Ventajas sobre webhook:
///   - No requiere URL pública ni ngrok en desarrollo
///   - Tolerante a reinicios de la app (cubre la ventana perdida)
///   - Idempotente: el campo MpPaymentId + Estado evita doble-proceso
/// </summary>
public class MercadoPagoPollerService : BackgroundService
{
    private readonly IServiceScopeFactory              _scopeFactory;
    private readonly IHttpClientFactory                _httpFactory;
    private readonly IConfiguration                    _config;
    private readonly ILogger<MercadoPagoPollerService> _logger;

    public MercadoPagoPollerService(
        IServiceScopeFactory              scopeFactory,
        IHttpClientFactory                httpFactory,
        IConfiguration                    config,
        ILogger<MercadoPagoPollerService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory  = httpFactory;
        _config       = config;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MercadoPago Poller iniciado");

        // Esperar 30 s para que la app termine de iniciar antes del primer ciclo
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervaloMinutos = _config.GetValue("MercadoPago:PollingIntervaloMinutos", 5);

            try
            {
                await PollAsync(intervaloMinutos, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "MP Poller: error inesperado en ciclo de polling");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervaloMinutos), stoppingToken);
        }

        _logger.LogInformation("MercadoPago Poller detenido");
    }

    // ─────────────────────────────────────────────────────────────────────────
    private async Task PollAsync(int intervaloMinutos, CancellationToken ct)
    {
        using var scope  = _scopeFactory.CreateScope();
        var params_      = scope.ServiceProvider.GetRequiredService<IParametrosService>();
        var pagos        = scope.ServiceProvider.GetRequiredService<IPagoEcommerceService>();
        var db           = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // ── 1. ¿Está habilitado MercadoPago? ────────────────────────────────
        var habilitado = await params_.GetValorAsync("ecommerce", "metodoPagoMercadoPago");
        if (habilitado != "1")
        {
            _logger.LogDebug("MP Poller: MercadoPago deshabilitado — ciclo omitido");
            return;
        }

        var accessToken = await params_.GetValorAsync("mercadopago", "accessToken");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogDebug("MP Poller: accessToken no configurado — ciclo omitido");
            return;
        }

        // ── 2. Armar ventana de búsqueda ─────────────────────────────────────
        // La ventana es 50 % más grande que el intervalo para cubrir solapamientos.
        // La idempotencia en BD evita procesar duplicados.
        var ventanaMinutos = (int)Math.Ceiling(intervaloMinutos * 1.5);

        var argOffset = TimeSpan.FromHours(-3);          // UTC-3 Argentina
        var ahora     = DateTimeOffset.UtcNow.ToOffset(argOffset);
        var desde     = ahora.AddMinutes(-ventanaMinutos);

        // MP espera formato ISO 8601 con offset
        const string mpFmt  = "yyyy-MM-ddTHH:mm:ss.fffzzz";
        var beginDate = Uri.EscapeDataString(desde.ToString(mpFmt));
        var endDate   = Uri.EscapeDataString(ahora.ToString(mpFmt));

        _logger.LogInformation(
            "MP Poller: buscando pagos aprobados — ventana {Ventana} min ({Desde} → {Hasta})",
            ventanaMinutos, desde.ToString("HH:mm:ss"), ahora.ToString("HH:mm:ss"));

        // ── 3. Consultar API de MercadoPago ──────────────────────────────────
        var http = _httpFactory.CreateClient("MercadoPago");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        string jsonResp;
        try
        {
            var url  = $"https://api.mercadopago.com/v1/payments/search" +
                       $"?status=approved&begin_date={beginDate}&end_date={endDate}&sort=date_approved&criteria=desc&limit=50";
            var resp = await http.GetAsync(url, ct);
            jsonResp = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "MP Poller: API respondió {Code}. Body: {Body}",
                    (int)resp.StatusCode, jsonResp);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MP Poller: error HTTP consultando API de MercadoPago");
            return;
        }

        // ── 4. Parsear resultados ─────────────────────────────────────────────
        using var doc = JsonDocument.Parse(jsonResp);
        if (!doc.RootElement.TryGetProperty("results", out var results))
        {
            _logger.LogDebug("MP Poller: respuesta sin 'results' — no hay pagos");
            return;
        }

        var total = results.GetArrayLength();
        _logger.LogInformation("MP Poller: {Total} pago(s) aprobado(s) encontrado(s)", total);

        if (total == 0) return;

        // ── 5. Procesar cada pago ─────────────────────────────────────────────
        foreach (var pago in results.EnumerateArray())
        {
            if (ct.IsCancellationRequested) break;

            // Extraer paymentId
            var paymentId = pago.TryGetProperty("id", out var idProp)
                ? idProp.GetInt64().ToString()
                : string.Empty;

            if (string.IsNullOrEmpty(paymentId)) continue;

            // Idempotencia: ¿ya registrado como aprobado?
            var yaAprobado = await db.CobrosEcommerce
                .AnyAsync(c => c.MpPaymentId == paymentId && c.Estado == "aprobado", ct);

            if (yaAprobado)
            {
                _logger.LogDebug(
                    "MP Poller: paymentId={PaymentId} ya procesado — omitiendo", paymentId);
                continue;
            }

            // external_reference para logging anticipado
            var extRef = pago.TryGetProperty("external_reference", out var erProp)
                ? erProp.GetString() ?? "-"
                : "-";

            _logger.LogInformation(
                "MP Poller: nuevo pago aprobado — paymentId={PaymentId} pedidoId={ExtRef}",
                paymentId, extRef);

            try
            {
                // Reutiliza la lógica existente: busca el cobro, confirma el pedido,
                // descuenta stock y envía emails
                await pagos.ProcesarNotificacionMercadoPagoAsync(paymentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "MP Poller: error procesando paymentId={PaymentId}", paymentId);
                // Continúa con el siguiente pago
            }
        }
    }
}
