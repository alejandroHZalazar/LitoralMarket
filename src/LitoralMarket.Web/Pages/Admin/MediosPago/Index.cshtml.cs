using System.Globalization;
using System.Text.RegularExpressions;
using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages.Admin.MediosPago;

/// <summary>
/// Medios de pago del checkout (contra reembolso, Mercado Pago, transferencia) y bandeja de
/// transferencias recibidas que el sistema no pudo asociar solo a un pedido.
/// </summary>
[Authorize(Roles = "admin")]
public class IndexModel : PageModel
{
    private static readonly Regex AliasValido = new(@"^[A-Za-z0-9.\-]{6,20}$", RegexOptions.Compiled);
    private static readonly Regex CvuValido   = new(@"^\d{22}$", RegexOptions.Compiled);

    private readonly IParametrosService               _params;
    private readonly IConciliacionTransferenciasService _conciliacion;
    private readonly IMercadoPagoOAuthService         _oauth;
    private readonly IVentaEcommerceService           _ventas;

    public IndexModel(IParametrosService parametros, IConciliacionTransferenciasService conciliacion,
        IMercadoPagoOAuthService oauth, IVentaEcommerceService ventas)
    {
        _params       = parametros;
        _conciliacion = conciliacion;
        _oauth        = oauth;
        _ventas       = ventas;
    }

    /// <summary>Estado del registro automático de ventas al confirmar pedidos.</summary>
    public DiagnosticoVentaEcommerce? DiagVenta { get; private set; }

    [BindProperty] public FormularioMedios Form { get; set; } = new();

    public bool MpConectado { get; private set; }
    /// <summary>false si todavía no se corrió sql/create_transferencias_mp.sql en esta base.</summary>
    public bool TablasListas { get; private set; }
    public string? Filtro { get; private set; }
    public List<TransferenciaMpDto> Transferencias { get; private set; } = new();
    public List<CobroTransferenciaAsignableDto> Asignables { get; private set; } = new();
    public int ParaRevisar { get; private set; }

    [TempData] public string? Exito { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task OnGetAsync(string? estado)
    {
        await CargarFormularioAsync();
        await CargarBandejaAsync(estado);
        DiagVenta = await _ventas.DiagnosticoAsync();
    }

    // ── Guardar configuración ───────────────────────────────────────────────
    public async Task<IActionResult> OnPostGuardarAsync()
    {
        var alias   = (Form.Alias   ?? string.Empty).Trim();
        var cvu     = Regex.Replace(Form.Cvu ?? string.Empty, @"\s", "");
        var titular = (Form.Titular ?? string.Empty).Trim();

        var errores = new List<string>();
        if (alias.Length > 0 && !AliasValido.IsMatch(alias))
            errores.Add("El alias debe tener entre 6 y 20 caracteres: letras, números, punto o guion.");
        if (cvu.Length > 0 && !CvuValido.IsMatch(cvu))
            errores.Add("El CVU debe tener exactamente 22 dígitos.");
        if (titular.Length > 100)
            errores.Add("El titular admite hasta 100 caracteres.");
        if (Form.VencimientoHoras is < 1 or > 168)
            errores.Add("El plazo para transferir debe estar entre 1 y 168 horas.");
        if (Form.PollingSegundos is < 30 or > 600)
            errores.Add("La frecuencia de detección debe estar entre 30 y 600 segundos.");

        if (Form.Transferencia)
        {
            if (alias.Length == 0 && cvu.Length == 0)
                errores.Add("Para habilitar la transferencia cargá el alias o el CVU de destino.");
            if (!(await _oauth.GetConnectionInfoAsync()).Conectado)
                errores.Add("Para habilitar la transferencia primero conectá la cuenta en Admin > Mercado Pago: sin esa conexión no se pueden detectar los pagos.");
            if (!await _conciliacion.TablasListasAsync())
                errores.Add("Faltan las tablas de transferencias en esta base: ejecutá sql/create_transferencias_mp.sql antes de habilitarla.");
        }

        if (errores.Count > 0)
        {
            Error = string.Join(" ", errores);
            return RedirectToPage();
        }

        static string Uno(bool b) => b ? "1" : "0";
        var inv = CultureInfo.InvariantCulture;

        await _params.SetValorAsync("ecommerce",   "metodoPagoReembolso",           Uno(Form.Reembolso));
        await _params.SetValorAsync("ecommerce",   "metodoPagoMercadoPago",         Uno(Form.MercadoPago));
        await _params.SetValorAsync("ecommerce",   "metodoPagoTransferenciaMP",     Uno(Form.Transferencia));
        await _params.SetValorAsync("ecommerce",   "transferenciaVencimientoHoras", Form.VencimientoHoras.ToString(inv));
        await _params.SetValorAsync("mercadopago", "transferenciaAlias",            alias);
        await _params.SetValorAsync("mercadopago", "transferenciaCvu",              cvu);
        await _params.SetValorAsync("mercadopago", "transferenciaTitular",          titular);
        await _params.SetValorAsync("mercadopago", "transferenciaPollingSegundos",  Form.PollingSegundos.ToString(inv));

        Exito = "Medios de pago guardados.";
        return RedirectToPage();
    }

    // ── Resolver transferencias ─────────────────────────────────────────────
    public async Task<IActionResult> OnPostAsignarAsync(int transferenciaId, int cobroId)
    {
        if (cobroId <= 0)
        {
            Error = "Elegí el pedido al que corresponde la transferencia.";
            return RedirectToPage();
        }

        var r = await _conciliacion.AsignarManualAsync(transferenciaId, cobroId);
        if (r.Ok) Exito = "Transferencia asignada: el pedido quedó confirmado.";
        else      Error = r.Error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostIgnorarAsync(int transferenciaId)
    {
        var r = await _conciliacion.IgnorarAsync(transferenciaId);
        if (r.Ok) Exito = "Transferencia marcada como ignorada.";
        else      Error = r.Error;
        return RedirectToPage();
    }

    // ── Carga ───────────────────────────────────────────────────────────────
    private async Task CargarFormularioAsync()
    {
        static int Entero(string? v, int predeterminado, int min, int max) =>
            int.TryParse(v?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max
                ? n : predeterminado;

        Form = new FormularioMedios
        {
            Reembolso        = await _params.GetValorAsync("ecommerce", "metodoPagoReembolso") == "1",
            MercadoPago      = await _params.GetValorAsync("ecommerce", "metodoPagoMercadoPago") == "1",
            Transferencia    = await _params.GetValorAsync("ecommerce", "metodoPagoTransferenciaMP") == "1",
            Alias            = await _params.GetValorAsync("mercadopago", "transferenciaAlias"),
            Cvu              = await _params.GetValorAsync("mercadopago", "transferenciaCvu"),
            Titular          = await _params.GetValorAsync("mercadopago", "transferenciaTitular"),
            VencimientoHoras = Entero(await _params.GetValorAsync("ecommerce", "transferenciaVencimientoHoras"), 24, 1, 168),
            PollingSegundos  = Entero(await _params.GetValorAsync("mercadopago", "transferenciaPollingSegundos"), 90, 30, 600)
        };

        MpConectado = (await _oauth.GetConnectionInfoAsync()).Conectado;
    }

    private async Task CargarBandejaAsync(string? estado)
    {
        // "revisar" agrupa todo lo que el administrador todavía puede resolver.
        Filtro = estado;

        TablasListas = await _conciliacion.TablasListasAsync();
        if (!TablasListas) return;   // sin tablas no hay nada que listar: la vista muestra el aviso

        var todas = await _conciliacion.ListarAsync(null, 300);

        ParaRevisar = todas.Count(t => t.Pendiente);
        Transferencias = estado switch
        {
            "revisar"                   => todas.Where(t => t.Pendiente).ToList(),
            TransferenciaMpEstado.Asignada => todas.Where(t => t.Estado == TransferenciaMpEstado.Asignada).ToList(),
            TransferenciaMpEstado.Ignorada => todas.Where(t => t.Estado == TransferenciaMpEstado.Ignorada).ToList(),
            _                           => todas
        };

        // Los cobros a los que se puede asignar solo hacen falta si hay algo para resolver.
        if (ParaRevisar > 0)
            Asignables = await _conciliacion.ListarCobrosAsignablesAsync();
    }

    public class FormularioMedios
    {
        public bool Reembolso     { get; set; }
        public bool MercadoPago   { get; set; }
        public bool Transferencia { get; set; }
        public string? Alias      { get; set; }
        public string? Cvu        { get; set; }
        public string? Titular    { get; set; }
        public int VencimientoHoras { get; set; } = 24;
        public int PollingSegundos  { get; set; } = 90;
    }
}
