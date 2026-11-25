using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Detección y conciliación de transferencias entrantes de Mercado Pago.
///
/// Mercado Pago NO informa quién transfiere ni permite una referencia (una transferencia a CVU llega
/// sin external_reference). La única forma automática de vincularla a un pedido es el IMPORTE EXACTO:
/// cada cobro de transferencia pendiente tiene un importe único (ver PagoEcommerceService).
///
/// Asignación automática solo si TODO coincide: importe exacto con un único cobro pendiente y
/// acreditación dentro de la ventana del cobro. Cualquier otro caso queda registrado, sin confirmar,
/// para que lo resuelva el administrador.
/// </summary>
public class ConciliacionTransferenciasService : IConciliacionTransferenciasService
{
    private const int  PollingPredeterminado = 90;
    private const int  PollingMin            = 30;
    private const int  PollingMax            = 600;
    private const int  PaginaTamano          = 100;
    private const int  MaxPaginas            = 5;
    private static readonly TimeSpan Solapamiento   = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan VentanaInicial = TimeSpan.FromHours(2);
    private static readonly TimeSpan VentanaMaxima  = TimeSpan.FromHours(48);
    private static readonly TimeSpan ToleranciaReloj = TimeSpan.FromMinutes(2);

    private readonly AppDbContext                              _db;
    private readonly IParametrosService                        _params;
    private readonly IPagoEcommerceService                     _pagos;
    private readonly IMercadoPagoOAuthService                  _oauth;
    private readonly IEmailService                             _email;
    private readonly IHttpClientFactory                        _httpFactory;
    private readonly ILogger<ConciliacionTransferenciasService> _logger;

    public ConciliacionTransferenciasService(
        AppDbContext db,
        IParametrosService parametros,
        IPagoEcommerceService pagos,
        IMercadoPagoOAuthService oauth,
        IEmailService email,
        IHttpClientFactory httpFactory,
        ILogger<ConciliacionTransferenciasService> logger)
    {
        _db          = db;
        _params      = parametros;
        _pagos       = pagos;
        _oauth       = oauth;
        _email       = email;
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    public async Task<bool> TablasListasAsync()
    {
        try
        {
            await _db.TransferenciasMp.AsNoTracking().Select(t => t.Id).FirstOrDefaultAsync();
            await _db.CobrosTransferencia.AsNoTracking().Select(t => t.FkCobro).FirstOrDefaultAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transferencias MP: faltan tablas — ¿se corrió sql/create_transferencias_mp.sql?");
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Ciclo completo
    // ─────────────────────────────────────────────────────────────
    public async Task<CicloConciliacionResultado> EjecutarCicloAsync(CancellationToken ct = default)
    {
        var polling = await GetPollingSegundosAsync();

        if (await _params.GetValorAsync("ecommerce", "metodoPagoTransferenciaMP") != "1")
            return new CicloConciliacionResultado(false, false, polling, 0, 0, 0);

        // Libera los importes de los cobros vencidos aunque Mercado Pago no responda.
        await _pagos.CancelarTransferenciasVencidasAsync();

        // GetAccessTokenAsync renueva el token si está por vencer (el servicio corre por meses).
        var token = await _oauth.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Transferencias MP: no hay access token — conectá la cuenta en Admin > Mercado Pago.");
            return new CicloConciliacionResultado(true, true, polling, 0, 0, 0);
        }

        var ahora = DateTimeOffset.UtcNow;
        var desde = await CalcularDesdeAsync(ahora);

        List<PagoMpRecibido> pagos;
        try
        {
            pagos = await ConsultarPagosAsync(token, desde, ahora, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // No se avanza la marca de agua: la próxima vuelta vuelve a pedir esta misma ventana.
            _logger.LogWarning(ex, "Transferencias MP: error consultando pagos a Mercado Pago");
            return new CicloConciliacionResultado(true, true, polling, 0, 0, 0);
        }

        var resultado = await ProcesarPagosAsync(pagos, ct);
        await AvanzarMarcaDeAguaAsync(ahora);
        return resultado with { PollingSegundos = polling };
    }

    // ─────────────────────────────────────────────────────────────
    // Registro + conciliación
    // ─────────────────────────────────────────────────────────────
    public async Task<CicloConciliacionResultado> ProcesarPagosAsync(
        IReadOnlyList<PagoMpRecibido> pagos, CancellationToken ct = default)
    {
        var polling = await GetPollingSegundosAsync();
        int detectadas = 0, asignadas = 0, paraRevisar = 0;
        var alertas = new List<string>();

        foreach (var pago in pagos.Where(EsTransferenciaEntrante))
        {
            ct.ThrowIfCancellationRequested();

            // Idempotencia: una transferencia ya registrada no se vuelve a procesar jamás.
            if (await _db.TransferenciasMp.AnyAsync(t => t.MpPaymentId == pago.Id, ct))
                continue;

            var t = new TransferenciaMp
            {
                MpPaymentId     = pago.Id,
                Monto           = pago.Monto,
                Moneda          = pago.Moneda,
                FechaAprobacion = pago.FechaAprobacion?.LocalDateTime,   // misma hora (del servidor) que fecha_creacion
                FechaDetectada  = DateTime.Now,
                PaymentMethodId = pago.PaymentMethodId,
                OperationType   = pago.OperationType,
                PayerId         = pago.PayerId,
                StatusMp        = pago.Status,
                Estado          = TransferenciaMpEstado.SinAsignar,
                JsonMp          = ResumenAuditoria(pago)
            };

            _db.TransferenciasMp.Add(t);
            try
            {
                await _db.SaveChangesAsync(ct);   // UNIQUE(mp_payment_id): si otro proceso la registró primero, falla
            }
            catch (DbUpdateException)
            {
                _db.Entry(t).State = EntityState.Detached;
                continue;
            }

            detectadas++;
            var alerta = await ConciliarAsync(t, ct);
            if (t.Estado == TransferenciaMpEstado.Asignada) asignadas++;
            if (alerta is not null) { paraRevisar++; alertas.Add(alerta); }
        }

        if (alertas.Count > 0)
        {
            try { await _email.EnviarAlertaTransferenciasAdminAsync(alertas); }
            catch (Exception ex) { _logger.LogWarning(ex, "Transferencias MP: no se pudo enviar la alerta al administrador"); }
        }

        if (detectadas > 0)
            _logger.LogInformation(
                "Transferencias MP: {Detectadas} nueva(s) — {Asignadas} asignada(s) solas, {Revisar} para revisar",
                detectadas, asignadas, paraRevisar);

        return new CicloConciliacionResultado(true, false, polling, detectadas, asignadas, paraRevisar);
    }

    /// <summary>
    /// Aplica las reglas de asignación a una transferencia ya registrada. Devuelve el texto de alerta
    /// para el administrador si requiere revisión, o null si se asignó sola o no corresponde a ningún pedido.
    /// </summary>
    private async Task<string?> ConciliarAsync(TransferenciaMp t, CancellationToken ct)
    {
        var aprobada = t.FechaAprobacion ?? DateTime.Now;

        // Los cobros pendientes son pocos: se comparan en memoria (importe exacto, sin depender del proveedor SQL).
        var pendientes = await CobrosAsync("pendiente", null, ct);
        var exactos    = pendientes.Where(c => c.Importe == t.Monto).ToList();

        if (exactos.Count == 1)
        {
            var c = exactos[0];
            var desde = c.Creado - ToleranciaReloj;

            if (aprobada < desde || aprobada > c.Vence)
                return await MarcarAsync(t, TransferenciaMpEstado.FueraDeTermino,
                    $"Importe {Fmt(t.Monto)} coincide con el pedido #{c.PedidoId}, pero se acreditó {aprobada:dd/MM HH:mm}, fuera de la ventana del cobro ({c.Creado:dd/MM HH:mm} a {c.Vence:dd/MM HH:mm}).", ct);

            try
            {
                var ok = await _pagos.ConfirmarCobroAsync(c.CobroId, c.PedidoId, t.MpPaymentId, "approved",
                    "transferencia", soloPendiente: true);

                if (!ok)
                    return await MarcarAsync(t, TransferenciaMpEstado.SinAsignar,
                        $"El cobro #{c.CobroId} del pedido #{c.PedidoId} ya no estaba pendiente cuando llegó la transferencia de {Fmt(t.Monto)}.", ct);

                t.Estado      = TransferenciaMpEstado.Asignada;
                t.FkCobro     = c.CobroId;
                t.AsignadaPor = "auto";
                t.Motivo      = $"Importe exacto: pedido #{c.PedidoId}.";
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Transferencias MP: {Monto} asignada al pedido #{PedidoId} (cobro #{CobroId}, pago {PaymentId})",
                    t.Monto, c.PedidoId, c.CobroId, t.MpPaymentId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Transferencias MP: error confirmando el pedido #{PedidoId} con el pago {PaymentId}", c.PedidoId, t.MpPaymentId);
                return await MarcarAsync(t, TransferenciaMpEstado.SinAsignar,
                    $"Importe {Fmt(t.Monto)} coincide con el pedido #{c.PedidoId}, pero falló la confirmación del pedido: {ex.Message}", ct);
            }
        }

        if (exactos.Count > 1)
            return await MarcarAsync(t, TransferenciaMpEstado.Ambigua,
                $"Importe {Fmt(t.Monto)} coincide con {exactos.Count} cobros pendientes (pedidos {string.Join(", ", exactos.Select(e => "#" + e.PedidoId))}).", ct);

        // Sin coincidencia exacta con un cobro vigente. Primero: ¿llegó tarde, a un cobro que ya venció?
        // (Es la coincidencia más específica: el importe es exactamente el que se le pidió a ese cliente.)
        var limiteVencidos = aprobada - TimeSpan.FromDays(7);
        var vencidos = (await CobrosAsync("cancelado", limiteVencidos, ct))
            .Where(c => c.Importe == t.Monto)
            .ToList();
        if (vencidos.Count > 0)
            return await MarcarAsync(t, TransferenciaMpEstado.FueraDeTermino,
                $"Importe {Fmt(t.Monto)} coincide con el pedido #{vencidos[0].PedidoId}, pero su plazo para transferir ya había vencido.", ct);

        // ¿Se parece a algún cobro pendiente (distinto en menos de $1,00)?
        var parecidos = pendientes
            .Where(c => Math.Abs(c.Importe - t.Monto) < 1m || Math.Abs(c.Total - t.Monto) < 1m)
            .Take(3)
            .ToList();
        if (parecidos.Count > 0)
            return await MarcarAsync(t, TransferenciaMpEstado.ImporteDistinto,
                $"Llegó {Fmt(t.Monto)}; se esperaba " + string.Join(" o ",
                    parecidos.Select(p => $"{Fmt(p.Importe)} (pedido #{p.PedidoId})")) + ".", ct);

        // Sin ningún pedido pendiente con ese importe: es una transferencia ajena a la tienda.
        // Se registra pero NO se avisa por email (una cuenta recibe muchas transferencias que no son de la tienda).
        t.Motivo = "Sin pedido pendiente con ese importe.";
        await _db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Cobro de transferencia con su importe y plazo, ya unidos (sin tracking).</summary>
    private sealed record CobroTr(int CobroId, int PedidoId, decimal Total, DateTime Creado, decimal Importe, DateTime Vence);

    /// <summary>Cobros de transferencia en un estado; con <paramref name="vencimientoPosteriorA"/> solo los que vencen después de esa fecha.</summary>
    private async Task<List<CobroTr>> CobrosAsync(string estado, DateTime? vencimientoPosteriorA, CancellationToken ct)
    {
        var q = _db.CobrosTransferencia.AsNoTracking().Where(t => t.Cobro!.Estado == estado);
        if (vencimientoPosteriorA is { } desde)
            q = q.Where(t => t.Vencimiento > desde);

        return await q
            .Select(t => new CobroTr(t.FkCobro, t.Cobro!.FkPedido, t.Cobro.Monto, t.Cobro.FechaCreacion,
                                     t.MontoTransferencia, t.Vencimiento))
            .ToListAsync(ct);
    }

    private async Task<string> MarcarAsync(TransferenciaMp t, string estado, string motivo, CancellationToken ct)
    {
        t.Estado = estado;
        t.Motivo = Cortar(motivo, 500);
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Transferencias MP: {Estado} — {Motivo} (pago {PaymentId})", estado, motivo, t.MpPaymentId);
        return $"{t.MpPaymentId}: {motivo}";
    }

    // ─────────────────────────────────────────────────────────────
    // Lectura de Mercado Pago
    // ─────────────────────────────────────────────────────────────
    /// <summary>Solo las transferencias entrantes aprobadas, en pesos y sin external_reference.</summary>
    internal static bool EsTransferenciaEntrante(PagoMpRecibido p) =>
        p.Status == "approved"
        && (string.IsNullOrEmpty(p.Moneda) || p.Moneda == "ARS")
        && string.IsNullOrEmpty(p.ExternalReference)    // con referencia es un pago de Checkout: lo maneja el flujo del webhook
        && (p.PaymentTypeId == "bank_transfer" || p.PaymentMethodId == "cvu" || p.OperationType == "money_transfer");

    private async Task<DateTimeOffset> CalcularDesdeAsync(DateTimeOffset ahora)
    {
        var marca = await _params.GetValorAsync("mercadopago", "transferenciaUltimaLectura");
        var desde = DateTimeOffset.TryParse(marca, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var m)
            ? m - Solapamiento
            : ahora - VentanaInicial;

        // Nunca más atrás de lo necesario para cubrir el cobro pendiente más antiguo.
        var masAntiguo = await _db.CobrosTransferencia.AsNoTracking()
            .Where(t => t.Cobro!.Estado == "pendiente")
            .OrderBy(t => t.Cobro!.FechaCreacion)
            .Select(t => (DateTime?)t.Cobro!.FechaCreacion)
            .FirstOrDefaultAsync();
        if (masAntiguo is not null)
        {
            var creado = new DateTimeOffset(masAntiguo.Value).AddMinutes(-2);
            if (creado < desde) desde = creado;
        }

        var limite = ahora - VentanaMaxima;
        return desde < limite ? limite : desde;
    }

    private async Task AvanzarMarcaDeAguaAsync(DateTimeOffset ahora)
    {
        // Se persiste a lo sumo cada 5 minutos para no escribir en la BD en cada vuelta;
        // el solapamiento de 10 minutos cubre el retraso.
        var marca = await _params.GetValorAsync("mercadopago", "transferenciaUltimaLectura");
        if (DateTimeOffset.TryParse(marca, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var m)
            && ahora - m < TimeSpan.FromMinutes(5))
            return;

        await _params.SetValorAsync("mercadopago", "transferenciaUltimaLectura",
            ahora.ToString("o", CultureInfo.InvariantCulture));
    }

    private async Task<List<PagoMpRecibido>> ConsultarPagosAsync(
        string token, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        var resultado = new List<PagoMpRecibido>();
        var http = _httpFactory.CreateClient("MercadoPago");

        // Mercado Pago espera la hora de Argentina con milisegundos y offset.
        var arg = TimeSpan.FromHours(-3);
        const string fmt = "yyyy-MM-ddTHH:mm:ss.fffzzz";
        var begin = Uri.EscapeDataString(desde.ToOffset(arg).ToString(fmt, CultureInfo.InvariantCulture));
        var end   = Uri.EscapeDataString(hasta.ToOffset(arg).ToString(fmt, CultureInfo.InvariantCulture));

        for (var pagina = 0; pagina < MaxPaginas; pagina++)
        {
            var url = "https://api.mercadopago.com/v1/payments/search" +
                      "?status=approved&range=date_approved&sort=date_approved&criteria=desc" +
                      $"&begin_date={begin}&end_date={end}&limit={PaginaTamano}&offset={pagina * PaginaTamano}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await http.SendAsync(req, ct);
            var cuerpo = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Mercado Pago respondió {(int)resp.StatusCode}: {Cortar(cuerpo, 300)}");

            using var doc = JsonDocument.Parse(cuerpo);
            if (!doc.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
                break;

            var enPagina = 0;
            foreach (var r in results.EnumerateArray())
            {
                enPagina++;
                var pago = Parsear(r);
                if (pago is not null) resultado.Add(pago);
            }

            if (enPagina < PaginaTamano) break;
        }

        return resultado;
    }

    internal static PagoMpRecibido? Parsear(JsonElement r)
    {
        static string? Str(JsonElement e, string nombre) =>
            e.TryGetProperty(nombre, out var v)
                ? v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.GetRawText(),
                    _                    => null
                }
                : null;

        var id = Str(r, "id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        decimal monto = 0m;
        if (r.TryGetProperty("transaction_amount", out var ta) && ta.ValueKind == JsonValueKind.Number)
            monto = ta.GetDecimal();

        DateTimeOffset? aprobada = null;
        if (DateTimeOffset.TryParse(Str(r, "date_approved"), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var fecha))
            aprobada = fecha;

        string? payerId = null;
        if (r.TryGetProperty("payer", out var payer) && payer.ValueKind == JsonValueKind.Object)
            payerId = Str(payer, "id");

        return new PagoMpRecibido(
            id, Math.Round(monto, 2), Str(r, "currency_id"), Str(r, "status"),
            Str(r, "payment_method_id"), Str(r, "payment_type_id"), Str(r, "operation_type"),
            payerId, Str(r, "external_reference"), aprobada);
    }

    /// <summary>Solo los campos usados para decidir: nada de nombre, documento ni email del pagador.</summary>
    private static string ResumenAuditoria(PagoMpRecibido p) => JsonSerializer.Serialize(new
    {
        id                = p.Id,
        status            = p.Status,
        transaction_amount = p.Monto,
        currency_id       = p.Moneda,
        payment_method_id = p.PaymentMethodId,
        payment_type_id   = p.PaymentTypeId,
        operation_type    = p.OperationType,
        date_approved     = p.FechaAprobacion?.ToString("o", CultureInfo.InvariantCulture),
        payer_id          = p.PayerId
    });

    // ─────────────────────────────────────────────────────────────
    // Consultas para el administrador
    // ─────────────────────────────────────────────────────────────
    public async Task<List<TransferenciaMpDto>> ListarAsync(string? estado, int maximo = 200)
    {
        var q = _db.TransferenciasMp.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(estado))
            q = q.Where(t => t.Estado == estado);

        var filas = await q.OrderByDescending(t => t.Id).Take(Math.Clamp(maximo, 1, 500)).ToListAsync();

        var cobroIds = filas.Where(t => t.FkCobro != null).Select(t => t.FkCobro!.Value).Distinct().ToList();
        var pedidos = cobroIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.CobrosEcommerce.AsNoTracking()
                .Where(c => cobroIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FkPedido);

        return filas.Select(t => new TransferenciaMpDto
        {
            Id              = t.Id,
            MpPaymentId     = t.MpPaymentId,
            Monto           = t.Monto,
            FechaAprobacion = t.FechaAprobacion,
            Estado          = t.Estado,
            Motivo          = t.Motivo,
            AsignadaPor     = t.AsignadaPor,
            FkCobro         = t.FkCobro,
            PedidoId        = t.FkCobro is { } c && pedidos.TryGetValue(c, out var p) ? p : null,
            Pendiente       = TransferenciaMpEstado.Pendientes.Contains(t.Estado)
        }).ToList();
    }

    public async Task<List<CobroTransferenciaAsignableDto>> ListarCobrosAsignablesAsync()
    {
        var limite = DateTime.Now.AddDays(-7);
        var filas = await _db.CobrosTransferencia.AsNoTracking()
            .Include(t => t.Cobro).ThenInclude(c => c!.Pedido).ThenInclude(p => p!.Cliente)
            .Where(t => (t.Cobro!.Estado == "pendiente" || (t.Cobro.Estado == "cancelado" && t.Cobro.FechaCreacion > limite))
                        && t.Cobro.Pedido != null && t.Cobro.Pedido.EstadoEcommerce == "pendiente_pago")
            .ToListAsync();

        return filas
            .OrderByDescending(t => t.Cobro!.Estado == "pendiente")
            .ThenByDescending(t => t.Cobro!.FechaCreacion)
            .Select(t => new CobroTransferenciaAsignableDto
            {
                CobroId            = t.FkCobro,
                PedidoId           = t.Cobro!.FkPedido,
                Cliente            = t.Cobro.Pedido!.Cliente?.NombreComercial ?? t.Cobro.Pedido.Cliente?.RazonSocial ?? t.Cobro.Pedido.NombreCliente,
                Total              = t.Cobro.Monto,
                MontoTransferencia = t.MontoTransferencia,
                Vencimiento        = t.Vencimiento,
                Estado             = t.Cobro.Estado
            })
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────
    // Resolución manual
    // ─────────────────────────────────────────────────────────────
    public async Task<ResultadoAsignacion> AsignarManualAsync(int transferenciaId, int cobroId)
    {
        var t = await _db.TransferenciasMp.AsNoTracking().FirstOrDefaultAsync(x => x.Id == transferenciaId);
        if (t is null) return ResultadoAsignacion.Fallo("La transferencia no existe.");
        if (!TransferenciaMpEstado.Pendientes.Contains(t.Estado))
            return ResultadoAsignacion.Fallo("Esa transferencia ya fue resuelta.");

        var cobro = await _db.CobrosEcommerce.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cobroId);
        if (cobro is null || cobro.Tipo != "transferencia")
            return ResultadoAsignacion.Fallo("El cobro elegido no existe o no es de transferencia.");
        if (cobro.Estado == "aprobado")
            return ResultadoAsignacion.Fallo("Ese cobro ya fue pagado.");

        var estadoPedido = await _db.Pedidos.Where(p => p.Id == cobro.FkPedido)
            .Select(p => p.EstadoEcommerce).FirstOrDefaultAsync();
        if (estadoPedido != "pendiente_pago")
            return ResultadoAsignacion.Fallo($"El pedido #{cobro.FkPedido} no está pendiente de pago (estado: {estadoPedido ?? "inexistente"}).");

        // Se reclama la transferencia primero (UPDATE condicional): dos administradores no pueden
        // asignar la misma transferencia a dos pedidos distintos.
        var estadoAnterior = t.Estado;
        var reclamada = await _db.TransferenciasMp
            .Where(x => x.Id == transferenciaId && x.FkCobro == null
                        && (x.Estado == TransferenciaMpEstado.SinAsignar || x.Estado == TransferenciaMpEstado.Ambigua
                            || x.Estado == TransferenciaMpEstado.ImporteDistinto || x.Estado == TransferenciaMpEstado.FueraDeTermino))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Estado,      TransferenciaMpEstado.Asignada)
                .SetProperty(x => x.FkCobro,     cobroId)
                .SetProperty(x => x.AsignadaPor, "admin"));
        if (reclamada == 0)
            return ResultadoAsignacion.Fallo("Esa transferencia ya fue resuelta por otra persona.");

        try
        {
            var ok = await _pagos.ConfirmarCobroAsync(cobroId, cobro.FkPedido, t.MpPaymentId, "approved",
                "transferencia-manual", soloPendiente: false);
            if (!ok)
            {
                await LiberarAsync(transferenciaId, estadoAnterior);
                return ResultadoAsignacion.Fallo("Ese cobro ya fue pagado.");
            }

            var nota = $"Asignada manualmente al pedido #{cobro.FkPedido}.";
            var motivo = string.IsNullOrWhiteSpace(t.Motivo) ? nota : Cortar($"{t.Motivo} | {nota}", 500);
            await _db.TransferenciasMp.Where(x => x.Id == transferenciaId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Motivo, motivo));

            _logger.LogInformation(
                "Transferencias MP: transferencia #{Id} (pago {PaymentId}) asignada manualmente al pedido #{PedidoId}",
                transferenciaId, t.MpPaymentId, cobro.FkPedido);
            return ResultadoAsignacion.Exito();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transferencias MP: falló la asignación manual de la transferencia #{Id}", transferenciaId);
            await LiberarAsync(transferenciaId, estadoAnterior);
            return ResultadoAsignacion.Fallo($"No se pudo confirmar el pedido: {ex.Message}");
        }
    }

    private Task<int> LiberarAsync(int transferenciaId, string estadoAnterior) =>
        _db.TransferenciasMp.Where(x => x.Id == transferenciaId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Estado,      estadoAnterior)
                .SetProperty(x => x.FkCobro,     (int?)null)
                .SetProperty(x => x.AsignadaPor, (string?)null));

    public async Task<ResultadoAsignacion> IgnorarAsync(int transferenciaId)
    {
        var filas = await _db.TransferenciasMp
            .Where(x => x.Id == transferenciaId && x.FkCobro == null
                        && (x.Estado == TransferenciaMpEstado.SinAsignar || x.Estado == TransferenciaMpEstado.Ambigua
                            || x.Estado == TransferenciaMpEstado.ImporteDistinto || x.Estado == TransferenciaMpEstado.FueraDeTermino))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, TransferenciaMpEstado.Ignorada));

        return filas > 0
            ? ResultadoAsignacion.Exito()
            : ResultadoAsignacion.Fallo("Esa transferencia ya fue resuelta.");
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────
    private async Task<int> GetPollingSegundosAsync()
    {
        var val = await _params.GetValorAsync("mercadopago", "transferenciaPollingSegundos");
        return int.TryParse(val?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var s)
               && s is >= PollingMin and <= PollingMax
            ? s
            : PollingPredeterminado;
    }

    private static string Fmt(decimal monto) => "$" + monto.ToString("N2", new CultureInfo("es-AR"));

    private static string Cortar(string s, int max) => s.Length <= max ? s : s[..max];
}
