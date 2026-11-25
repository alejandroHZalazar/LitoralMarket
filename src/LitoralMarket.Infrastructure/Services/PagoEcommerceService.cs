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
    private readonly IDbContextFactory<AppDbContext> _ctxFactory;

    public PagoEcommerceService(
        AppDbContext                  db,
        IParametrosService            parametros,
        IPedidoService                pedidos,
        IHttpClientFactory            httpFactory,
        IConfiguration                config,
        ILogger<PagoEcommerceService> logger,
        IServiceScopeFactory          scopeFactory,
        IDbContextFactory<AppDbContext> ctxFactory)
    {
        _db           = db;
        _params       = parametros;
        _pedidos      = pedidos;
        _httpFactory  = httpFactory;
        _config       = config;
        _logger       = logger;
        _scopeFactory = scopeFactory;
        _ctxFactory   = ctxFactory;
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
    public async Task<(bool reembolso, bool mercadoPago, bool transferencia)> ObtenerMetodosHabilitadosAsync()
    {
        var reembolso   = await _params.GetValorAsync("ecommerce", "metodoPagoReembolso")   == "1";
        var mercadoPago = await _params.GetValorAsync("ecommerce", "metodoPagoMercadoPago") == "1";

        // Transferencia: solo con el flag en '1' Y con un destino cargado (alias o CVU);
        // sin destino no hay a dónde transferir.
        var transferencia = false;
        if (await _params.GetValorAsync("ecommerce", "metodoPagoTransferenciaMP") == "1")
        {
            var alias = await _params.GetValorAsync("mercadopago", "transferenciaAlias");
            var cvu   = await _params.GetValorAsync("mercadopago", "transferenciaCvu");
            transferencia = !string.IsNullOrWhiteSpace(alias) || !string.IsNullOrWhiteSpace(cvu);
        }

        return (reembolso, mercadoPago, transferencia);
    }

    /// <summary>ecommerce/transferenciaVencimientoHoras: entero entre 1 y 168; predeterminado 24.</summary>
    private async Task<int> GetVencimientoTransferenciaHorasAsync()
    {
        var val = await _params.GetValorAsync("ecommerce", "transferenciaVencimientoHoras");
        return int.TryParse(val?.Trim(), System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out var h) && h is >= 1 and <= 168
            ? h
            : 24;
    }

    // ─────────────────────────────────────────────────────────────
    // Cobro existente
    // ─────────────────────────────────────────────────────────────
    public async Task<CobroEcommerceDto?> ObtenerCobroPorPedidoAsync(int pedidoId)
    {
        // Contexto propio de la factory: lectura pura → puede ejecutarse en paralelo
        // con el resumen del pedido en la pantalla de forma de pago.
        await using var db = await _ctxFactory.CreateDbContextAsync();
        var cobro = await db.CobrosEcommerce
            .Where(c => c.FkPedido == pedidoId)
            .OrderByDescending(c => c.FechaCreacion)
            .FirstOrDefaultAsync();

        if (cobro is null) return null;

        var dto = ToDto(cobro);

        // Los datos propios de la transferencia viven en otra tabla: solo se consulta (y solo existe
        // necesariamente) cuando el cobro es de ese tipo; el resto de los flujos no la toca.
        if (cobro.Tipo == "transferencia")
        {
            var det = await db.CobrosTransferencia.AsNoTracking()
                .FirstOrDefaultAsync(t => t.FkCobro == cobro.Id);
            dto.MontoTransferencia = det?.MontoTransferencia;
            dto.Vencimiento        = det?.Vencimiento;
        }

        return dto;
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
            mpInitPoint    = root.GetProperty("init_point").GetString() ?? string.Empty;
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
    // Transferencia de Mercado Pago — crear cobro con importe único
    // ─────────────────────────────────────────────────────────────
    public async Task<ResultadoPagoDto> CrearCobroTransferenciaAsync(int pedidoId)
    {
        try
        {
            return await CrearCobroTransferenciaCoreAsync(pedidoId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Típico: se habilitó el flag sin haber corrido sql/create_transferencias_mp.sql (faltan tablas).
            // El cliente ve un mensaje claro en vez de un 500 y puede elegir otro método.
            _logger.LogError(ex,
                "Transferencia: no se pudo crear el cobro del pedido #{Id}. ¿Se corrió sql/create_transferencias_mp.sql?", pedidoId);
            return Error("No pudimos habilitar el pago por transferencia en este momento. Elegí otro método de pago o intentá más tarde.", pedidoId);
        }
    }

    private async Task<ResultadoPagoDto> CrearCobroTransferenciaCoreAsync(int pedidoId)
    {
        if (!(await ObtenerMetodosHabilitadosAsync()).transferencia)
            return Error("El pago por transferencia no está disponible.", pedidoId);

        var pedido = await _db.Pedidos.FirstOrDefaultAsync(p => p.Id == pedidoId);
        if (pedido is null)
            return Error("Pedido no encontrado", pedidoId);

        if (pedido.EstadoEcommerce != "pendiente_pago")
            return Error($"El pedido no está listo para pagar (estado: {pedido.EstadoEcommerce})", pedidoId);

        var total = Math.Round(pedido.Total ?? 0m, 2);
        if (total < 2m)
            return Error("El importe del pedido es muy bajo para pagarlo por transferencia.", pedidoId);

        // Primero se liberan los importes de los cobros vencidos.
        await CancelarTransferenciasVencidasAsync();

        // Doble envío del formulario / recarga: si ya hay un cobro vigente, se devuelve el mismo.
        var existente = await _db.CobrosTransferencia.AsNoTracking()
            .Include(t => t.Cobro)
            .Where(t => t.Cobro!.FkPedido == pedidoId && t.Cobro.Estado == "pendiente")
            .OrderByDescending(t => t.FkCobro)
            .FirstOrDefaultAsync();
        if (existente is not null)
            return ResultadoTransferencia(existente, emailEnviado: false);

        var horas = await GetVencimientoTransferenciaHorasAsync();

        // El importe es el total menos entre $0,01 y $0,99, distinto del de cualquier otro cobro de
        // transferencia pendiente. El índice UNIQUE (monto_activo) garantiza la unicidad aun con dos
        // pedidos simultáneos: si otro tomó el mismo importe justo antes, se descarta y se prueba el siguiente.
        var descartados = new HashSet<decimal>();
        for (var intento = 0; intento < 5; intento++)
        {
            var usados = (await _db.CobrosTransferencia.AsNoTracking()
                    .Where(t => t.MontoActivo != null)
                    .Select(t => t.MontoActivo!.Value)
                    .ToListAsync())
                .ToHashSet();

            // Cuarentena: el importe de un cobro que venció hace poco NO se reutiliza enseguida. Si el
            // cliente del pedido vencido transfiere tarde, esa transferencia no debe confirmar a otro pedido
            // que casualmente tomó el mismo importe. Solo se reutiliza si no queda ninguno libre.
            var limiteCuarentena = DateTime.Now.AddDays(-7);
            var enCuarentena = (await _db.CobrosTransferencia.AsNoTracking()
                    .Where(t => t.Cobro!.Estado == "cancelado" && t.Vencimiento > limiteCuarentena)
                    .Select(t => t.MontoTransferencia)
                    .ToListAsync())
                .ToHashSet();

            decimal? elegido = null, respaldo = null;
            for (var centavos = 1; centavos <= 99; centavos++)
            {
                var candidato = total - centavos / 100m;
                if (usados.Contains(candidato) || descartados.Contains(candidato)) continue;   // en uso: nunca
                respaldo ??= candidato;
                if (!enCuarentena.Contains(candidato)) { elegido = candidato; break; }
            }
            elegido ??= respaldo;

            if (elegido is null)
                return Error("En este momento no podemos generar el importe para transferir. Probá de nuevo en unos minutos o elegí otro método de pago.", pedidoId);

            var ahora = DateTime.Now;
            var cobro = new CobroEcommerce
            {
                FkPedido      = pedidoId,
                Tipo          = "transferencia",
                Estado        = "pendiente",
                Monto         = total,
                Concepto      = $"Pedido #{pedidoId} — Transferencia Mercado Pago",
                FechaCreacion = ahora
            };
            var detalle = new CobroTransferencia
            {
                Cobro               = cobro,
                MontoTransferencia  = elegido.Value,
                MontoActivo         = elegido.Value,
                Vencimiento         = ahora.AddHours(horas)
            };

            // Un solo SaveChanges = una sola transacción: o quedan el cobro y su detalle, o ninguno.
            _db.CobrosEcommerce.Add(cobro);
            _db.CobrosTransferencia.Add(detalle);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // Otro pedido tomó este importe entre la lectura y el INSERT: se descarta y se reintenta.
                _db.Entry(detalle).State = EntityState.Detached;
                _db.Entry(cobro).State   = EntityState.Detached;
                descartados.Add(elegido.Value);
                _logger.LogWarning(ex,
                    "Transferencia: importe {Monto} ya tomado para pedido #{Id} (intento {N}) — reintentando",
                    elegido, pedidoId, intento + 1);
                continue;
            }

            var cobroId = cobro.Id;
            EmailFireAndForget(e => e.EnviarInstruccionesTransferenciaAsync(pedidoId, cobroId), pedidoId, "instrucciones-transferencia");
            EmailFireAndForget(e => e.EnviarNotificacionAdminAsync(pedidoId),                   pedidoId, "admin-transferencia");

            return ResultadoTransferencia(detalle, emailEnviado: true);
        }

        return Error("No pudimos reservar el importe para transferir. Probá de nuevo en unos minutos.", pedidoId);
    }

    public async Task<int> CancelarTransferenciasVencidasAsync()
    {
        var ahora = DateTime.Now;

        // Cobros con importe todavía "activo" y plazo vencido.
        var ids = await _db.CobrosTransferencia.AsNoTracking()
            .Where(t => t.MontoActivo != null && t.Vencimiento < ahora)
            .Select(t => t.FkCobro)
            .ToListAsync();
        if (ids.Count == 0) return 0;

        await using var tx = await _db.Database.BeginTransactionAsync();

        var n = await _db.CobrosEcommerce
            .Where(c => ids.Contains(c.Id) && c.Estado == "pendiente")
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Estado,   "cancelado")
                .SetProperty(c => c.MpStatus, "vencido"));

        // Se libera el importe de todos (los que ya no estaban pendientes tampoco deben retenerlo).
        await _db.CobrosTransferencia
            .Where(t => ids.Contains(t.FkCobro))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MontoActivo, (decimal?)null));

        await tx.CommitAsync();

        if (n > 0)
            _logger.LogInformation("Transferencia: {N} cobro(s) vencido(s) cancelado(s) — importes liberados", n);
        return n;
    }

    // ─────────────────────────────────────────────────────────────
    // Confirmación única de un cobro aprobado
    // ─────────────────────────────────────────────────────────────
    public async Task<bool> ConfirmarCobroAsync(int cobroId, int pedidoId, string? mpPaymentId, string? mpStatus,
        string origen, bool soloPendiente = true)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();

        // UPDATE condicional atómico: si otro proceso (webhook, poller, página, admin) ya aprobó el
        // cobro, no se afecta ninguna fila y no se repite el descuento de stock ni los emails.
        var ahora = DateTime.Now;
        var candidatos = _db.CobrosEcommerce.Where(c => c.Id == cobroId && c.FkPedido == pedidoId);
        candidatos = soloPendiente
            ? candidatos.Where(c => c.Estado == "pendiente")
            : candidatos.Where(c => c.Estado != "aprobado");

        var filas = await candidatos.ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Estado,      "aprobado")
            .SetProperty(c => c.MpPaymentId, mpPaymentId)
            .SetProperty(c => c.MpStatus,    mpStatus)
            .SetProperty(c => c.FechaPago,   ahora));

        if (filas == 0)
        {
            await tx.RollbackAsync();
            _logger.LogInformation(
                "ConfirmarCobro[{Origen}]: cobro #{CobroId} (pedido #{PedidoId}) ya no estaba pendiente — se omite",
                origen, cobroId, pedidoId);
            return false;
        }

        // Un cobro de transferencia que se aprueba libera su importe "activo". Solo en ese caso se toca
        // la tabla de transferencias: los cobros de MercadoPago/reembolso no dependen de ella.
        var tipo = await _db.CobrosEcommerce.Where(c => c.Id == cobroId).Select(c => c.Tipo).FirstOrDefaultAsync();
        if (tipo == "transferencia")
            await _db.CobrosTransferencia
                .Where(t => t.FkCobro == cobroId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.MontoActivo, (decimal?)null));

        // Mismo camino que el pago por QR: estado confirmado + descuento de stock + movimiento tipo 3.
        // Si falla, la excepción sale y el cobro vuelve a 'pendiente' (rollback al liberar la transacción).
        await _pedidos.ConfirmarPagoAsync(pedidoId);
        await tx.CommitAsync();

        _logger.LogInformation(
            "ConfirmarCobro[{Origen}]: cobro #{CobroId} aprobado — pedido #{PedidoId} confirmado (pago {PaymentId})",
            origen, cobroId, pedidoId, mpPaymentId);

        // Emails en background, solo cuando ESTA llamada fue la que confirmó.
        EmailFireAndForget(e => e.EnviarConfirmacionPedidoAsync(pedidoId), pedidoId, $"confirmacion-{origen}");
        EmailFireAndForget(e => e.EnviarNotificacionAdminAsync(pedidoId),  pedidoId, $"admin-{origen}");
        return true;
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
        if (status == "approved")
        {
            _logger.LogInformation(
                "MP Webhook: pago aprobado — confirmando pedido #{PedidoId}", pedidoId);

            // Confirmación única e idempotente (UPDATE condicional + transacción + emails una sola vez).
            // soloPendiente:false → también acepta un cobro 'rechazado' que luego se pagó con otra tarjeta.
            var confirmado = await ConfirmarCobroAsync(cobro.Id, pedidoId, paymentId, status, "mp", soloPendiente: false);
            if (!confirmado)
                _logger.LogInformation(
                    "MP Webhook: pedido #{PedidoId} paymentId={PaymentId} ya estaba aprobado — notificación repetida", pedidoId, paymentId);
            return;
        }

        cobro.MpPaymentId = paymentId;
        cobro.MpStatus    = status;

        if (status is "rejected" or "cancelled")
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

    /// <summary>Resultado de un cobro de transferencia. <paramref name="detalle"/> debe traer <c>Cobro</c> cargado.</summary>
    private static ResultadoPagoDto ResultadoTransferencia(CobroTransferencia detalle, bool emailEnviado) => new()
    {
        Exito              = true,
        PedidoId           = detalle.Cobro!.FkPedido,
        CobroId            = detalle.FkCobro,
        Tipo               = "transferencia",
        MontoTransferencia = detalle.MontoTransferencia,
        Vencimiento        = detalle.Vencimiento,
        EmailEnviado       = emailEnviado
    };
}
