using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LitoralMarket.Infrastructure.Services;

public class PagoEcommerceService : IPagoEcommerceService
{
    private readonly AppDbContext                    _db;
    private readonly IParametrosService              _params;
    private readonly IPedidoService                  _pedidos;
    private readonly IHttpClientFactory              _httpFactory;
    private readonly IConfiguration                  _config;
    private readonly ILogger<PagoEcommerceService>   _logger;
    private readonly IServiceScopeFactory            _scopeFactory;

    public PagoEcommerceService(
        AppDbContext                  db,
        IParametrosService            parametros,
        IPedidoService                pedidos,
        IHttpClientFactory            httpFactory,
        IConfiguration                config,
        ILogger<PagoEcommerceService> logger,
        IServiceScopeFactory          scopeFactory)
    {
        _db           = db;
        _params       = parametros;
        _pedidos      = pedidos;
        _httpFactory  = httpFactory;
        _config       = config;
        _logger       = logger;
        _scopeFactory = scopeFactory;
    }

    // ─────────────────────────────────────────────────────────────
    // Email fire-and-forget: crea su propio scope para que el DbContext
    // no quede disposed cuando termina el request HTTP que lo originó.
    // ─────────────────────────────────────────────────────────────
    private void EmailFireAndForget(
        Func<IEmailService, Task> accion, int pedidoId, string descripcion)
    {
        _ = Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
            try
            {
                await accion(email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "EmailFireAndForget({Desc}): error enviando email para pedido #{Id}",
                    descripcion, pedidoId);
            }
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Métodos habilitados
    // ─────────────────────────────────────────────────────────────
    public async Task<(bool reembolso, bool mercadoPago)> ObtenerMetodosHabilitadosAsync()
    {
        var reembolso   = await _params.GetValorAsync("ecommerce", "metodoPagoReembolso")   == "1";
        var mercadoPago = await _params.GetValorAsync("ecommerce", "metodoPagoMercadoPago") == "1";
        return (reembolso, mercadoPago);
    }

    // ─────────────────────────────────────────────────────────────
    // Cobro existente
    // ─────────────────────────────────────────────────────────────
    public async Task<CobroEcommerceDto?> ObtenerCobroPorPedidoAsync(int pedidoId)
    {
        var cobro = await _db.CobrosEcommerce
            .Where(c => c.FkPedido == pedidoId)
            .OrderByDescending(c => c.FechaCreacion)
            .FirstOrDefaultAsync();

        if (cobro is null) return null;

        return ToDto(cobro);
    }

    // ─────────────────────────────────────────────────────────────
    // Contra reembolso
    // ─────────────────────────────────────────────────────────────
    public async Task<ResultadoPagoDto> ProcesarReembolsoAsync(int pedidoId)
    {
        var pedido = await _db.Pedidos
            .Include(p => p.Detalles)
            .Include(p => p.Cliente)
            .FirstOrDefaultAsync(p => p.Id == pedidoId);

        if (pedido is null)
            return Error("Pedido no encontrado", pedidoId);

        if (pedido.EstadoEcommerce != "pendiente_pago")
            return Error($"El pedido no está listo para pagar (estado: {pedido.EstadoEcommerce})", pedidoId);

        // Crear cobro
        var cobro = new CobroEcommerce
        {
            FkPedido      = pedidoId,
            Tipo          = "reembolso",
            Estado        = "pendiente",
            Monto         = pedido.Total ?? 0,
            Concepto      = $"Pedido #{pedidoId} — Pago contra reembolso",
            FechaCreacion = DateTime.Now
        };
        _db.CobrosEcommerce.Add(cobro);
        await _db.SaveChangesAsync();

        // El pedido queda en pendiente_pago; la confirmación y el descuento de stock
        // se realizarán desde el módulo de cobros cuando el administrador registre el pago.

        // Notificaciones de email en background — no bloqueamos el hilo HTTP.
        // IEmailService crea su propio scope (IServiceScopeFactory) para evitar
        // que el DbContext quede disposed al terminar el request.
        EmailFireAndForget(e => e.EnviarConfirmacionPedidoAsync(pedidoId), pedidoId, "confirmacion-reembolso");
        EmailFireAndForget(e => e.EnviarNotificacionAdminAsync(pedidoId),  pedidoId, "admin-reembolso");

        return new ResultadoPagoDto
        {
            Exito        = true,
            PedidoId     = pedidoId,
            CobroId      = cobro.Id,
            Tipo         = "reembolso",
            EmailEnviado = true   // se está enviando en background
        };
    }

    // ─────────────────────────────────────────────────────────────
    // MercadoPago — crear preferencia
    // ─────────────────────────────────────────────────────────────
    public async Task<ResultadoPagoDto> CrearPreferenciaMercadoPagoAsync(int pedidoId)
    {
        var pedido = await _db.Pedidos
            .Include(p => p.Detalles)
            .Include(p => p.Cliente)
            .FirstOrDefaultAsync(p => p.Id == pedidoId);

        if (pedido is null)
            return Error("Pedido no encontrado", pedidoId);

        if (pedido.EstadoEcommerce != "pendiente_pago")
            return Error($"El pedido no está listo para pagar (estado: {pedido.EstadoEcommerce})", pedidoId);

        var accessToken = await _params.GetValorAsync("mercadopago", "accessToken");

        // En desarrollo, MercadoPago:UrlBase en appsettings.Development.json
        // permite usar el túnel de ngrok sin tocar la BD.
        var urlBase =
            _config["MercadoPago:UrlBase"]
            ?? await _params.GetValorAsync("mercadopago", "urlBase")
            ?? "https://localhost";

        if (string.IsNullOrWhiteSpace(accessToken))
            return Error("MercadoPago no configurado. Configurá el Access Token en parámetros.", pedidoId);

        // Construir preferencia
        // MP Argentina requiere offset -03:00 y milisegundos en el formato de fecha.
        var argOffset  = TimeSpan.FromHours(-3);
        var ahoraArg   = DateTimeOffset.UtcNow.ToOffset(argOffset);
        var expiracion = ahoraArg.AddDays(2);
        const string mpFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";

        var items = pedido.Detalles.Select(d => new
        {
            title       = d.Descripcion ?? "Producto",
            quantity    = (int)Math.Ceiling(d.Cantidad ?? 1),
            unit_price  = Math.Round(d.PrecioConIva ?? 0, 2),
            currency_id = "ARS"
        }).ToList<object>();

        // Agregar envío como ítem si hay costo
        if ((pedido.CostoEnvio ?? 0) > 0)
        {
            items.Add(new
            {
                title       = "Costo de envío",
                quantity    = 1,
                unit_price  = Math.Round(pedido.CostoEnvio ?? 0, 2),
                currency_id = "ARS"
            });
        }

        var preference = new
        {
            items,
            external_reference   = pedidoId.ToString(),
            expires              = true,
            expiration_date_from = ahoraArg.ToString(mpFormat),
            expiration_date_to   = expiracion.ToString(mpFormat),
            back_urls = new
            {
                success = $"{urlBase}/pago-resultado?pedidoId={pedidoId}&resultado=ok",
                failure = $"{urlBase}/pago-resultado?pedidoId={pedidoId}&resultado=error",
                pending = $"{urlBase}/pago-resultado?pedidoId={pedidoId}&resultado=pendiente"
            },
            auto_return      = "approved",
            notification_url = $"{urlBase}/api/mp-webhook",
            // Forzar modo estándar (no marketplace) para que funcione con tokens OAuth
            // en sandbox sin requerir aprobación extra de marketplace por parte de MP.
            marketplace      = "NONE"
        };

        // Llamar API de MercadoPago
        string mpInitPoint;
        string mpPreferenceId;
        try
        {
            var http    = _httpFactory.CreateClient("MercadoPago");
            var json    = JsonSerializer.Serialize(preference);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await http.PostAsync(
                "https://api.mercadopago.com/checkout/preferences", content);

            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return Error($"Error al crear preferencia MP: {responseBody}", pedidoId);

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            mpPreferenceId = root.GetProperty("id").GetString() ?? string.Empty;

            // En cuentas de prueba MP devuelve sandbox_init_point.
            // En producción solo existe init_point. Usamos sandbox si está disponible.
            var sandboxPoint = root.TryGetProperty("sandbox_init_point", out var sbp)
                ? sbp.GetString() : null;
            mpInitPoint = (!string.IsNullOrWhiteSpace(sandboxPoint)
                ? sandboxPoint
                : root.GetProperty("init_point").GetString()) ?? string.Empty;
        }
        catch (Exception ex)
        {
            return Error($"Error de comunicación con MercadoPago: {ex.Message}", pedidoId);
        }

        // Guardar cobro
        var cobro = new CobroEcommerce
        {
            FkPedido          = pedidoId,
            Tipo              = "mercadopago",
            Estado            = "pendiente",
            Monto             = pedido.Total ?? 0,
            Concepto          = $"Pedido #{pedidoId} — MercadoPago",
            MpPreferenceId    = mpPreferenceId,
            MpLinkPago        = mpInitPoint,
            MpFechaExpiracion = expiracion.DateTime,
            FechaCreacion     = DateTime.Now
        };
        _db.CobrosEcommerce.Add(cobro);
        await _db.SaveChangesAsync();

        // Email con link de pago en background (descarga QR + genera PDF + SMTP).
        // Capturamos cobro.Id antes de que el scope del pedido pueda cerrarse.
        var cobroId = cobro.Id;
        EmailFireAndForget(e => e.EnviarLinkMercadoPagoAsync(pedidoId, cobroId), pedidoId, "link-mp");
        EmailFireAndForget(e => e.EnviarNotificacionAdminAsync(pedidoId),        pedidoId, "admin-mp");

        return new ResultadoPagoDto
        {
            Exito             = true,
            PedidoId          = pedidoId,
            CobroId           = cobro.Id,
            Tipo              = "mercadopago",
            MpLinkPago        = mpInitPoint,
            MpFechaExpiracion = cobro.MpFechaExpiracion,
            EmailEnviado      = true   // se está enviando en background
        };
    }

    // ─────────────────────────────────────────────────────────────
    // MercadoPago — webhook
    // ─────────────────────────────────────────────────────────────
    public async Task ProcesarNotificacionMercadoPagoAsync(string paymentId)
    {
        _logger.LogInformation("MP Webhook → procesando paymentId={PaymentId}", paymentId);

        // ── 1. Access token ────────────────────────────────────────────
        var accessToken = await _params.GetValorAsync("mercadopago", "accessToken");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogWarning("MP Webhook: accessToken no configurado — se ignora paymentId={PaymentId}", paymentId);
            return;
        }

        // ── 2. Consultar pago en la API de MercadoPago ─────────────────
        string status;
        string externalRef;
        string responseBody = string.Empty;
        try
        {
            var http = _httpFactory.CreateClient("MercadoPago");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            var resp = await http.GetAsync(
                $"https://api.mercadopago.com/v1/payments/{paymentId}");

            responseBody = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "MP Webhook: API respondió {Code} para paymentId={PaymentId}. Body: {Body}",
                    (int)resp.StatusCode, paymentId, responseBody);
                return;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            status      = root.TryGetProperty("status", out var sp)
                          ? sp.GetString() ?? string.Empty
                          : string.Empty;

            externalRef = root.TryGetProperty("external_reference", out var er)
                          ? er.GetString() ?? string.Empty
                          : string.Empty;

            _logger.LogInformation(
                "MP Webhook: paymentId={PaymentId} status={Status} external_reference={ExtRef}",
                paymentId, status, externalRef);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MP Webhook: error consultando API para paymentId={PaymentId}. Body: {Body}",
                paymentId, responseBody);
            return;
        }

        // ── 3. Resolver pedidoId desde external_reference ──────────────
        if (!int.TryParse(externalRef, out var pedidoId))
        {
            _logger.LogWarning(
                "MP Webhook: external_reference='{ExtRef}' no es un pedidoId válido — paymentId={PaymentId}",
                externalRef, paymentId);
            return;
        }

        // ── 4. Buscar cobro en la BD ────────────────────────────────────
        var cobro = await _db.CobrosEcommerce
            .Where(c => c.FkPedido == pedidoId && c.Tipo == "mercadopago")
            .OrderByDescending(c => c.FechaCreacion)
            .FirstOrDefaultAsync();

        if (cobro is null)
        {
            _logger.LogWarning(
                "MP Webhook: no se encontró cobro para pedidoId={PedidoId} paymentId={PaymentId}",
                pedidoId, paymentId);
            return;
        }

        // ── 5. Actualizar cobro y procesar según estado ─────────────────
        cobro.MpPaymentId = paymentId;
        cobro.MpStatus    = status;

        if (status == "approved")
        {
            cobro.Estado    = "aprobado";
            cobro.FechaPago = DateTime.Now;
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "MP Webhook: pago aprobado — confirmando pedido #{PedidoId}", pedidoId);

            await _pedidos.ConfirmarPagoAsync(pedidoId);

            // Emails en background: no bloqueamos el hilo de polling ni el webhook.
            // Un timeout de 14 s en el cliente JS no puede cortar el envío de email.
            EmailFireAndForget(e => e.EnviarConfirmacionPedidoAsync(pedidoId), pedidoId, "confirmacion-mp-webhook");
            EmailFireAndForget(e => e.EnviarNotificacionAdminAsync(pedidoId),  pedidoId, "admin-mp-webhook");
        }
        else if (status is "rejected" or "cancelled")
        {
            cobro.Estado = status == "rejected" ? "rechazado" : "cancelado";
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "MP Webhook: pago {Status} — pedido #{PedidoId}", status, pedidoId);
        }
        else
        {
            // pending, in_process, authorized, etc.
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "MP Webhook: pago en estado intermedio '{Status}' — pedido #{PedidoId}", status, pedidoId);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────
    private static ResultadoPagoDto Error(string msg, int pedidoId) =>
        new() { Exito = false, Error = msg, PedidoId = pedidoId };

    private static CobroEcommerceDto ToDto(CobroEcommerce c) => new()
    {
        Id                = c.Id,
        FkPedido          = c.FkPedido,
        Tipo              = c.Tipo,
        Estado            = c.Estado,
        Monto             = c.Monto,
        Concepto          = c.Concepto,
        MpPreferenceId    = c.MpPreferenceId,
        MpLinkPago        = c.MpLinkPago,
        MpFechaExpiracion = c.MpFechaExpiracion,
        FechaCreacion     = c.FechaCreacion
    };
}
