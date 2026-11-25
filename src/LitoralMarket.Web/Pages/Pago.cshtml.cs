using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

public class PagoPageModel : PageModel
{
    private readonly IPagoEcommerceService _pagos;
    private readonly IPedidoService        _pedidos;
    private readonly IParametrosService    _params;

    public PagoPageModel(IPagoEcommerceService pagos, IPedidoService pedidos, IParametrosService parametros)
    {
        _pagos   = pagos;
        _pedidos = pedidos;
        _params  = parametros;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public PedidoResumenDto?  Pedido             { get; private set; }
    public ResultadoPagoDto?  Resultado          { get; private set; }
    public bool               MetodoReembolso    { get; private set; }
    public bool               MetodoMercadoPago  { get; private set; }
    public bool               MetodoTransferencia { get; private set; }
    public bool               PagoYaProcesado    { get; private set; }
    public CobroEcommerceDto? CobroExistente     { get; private set; }
    /// <summary>Teléfono normalizado para link wa.me (null si no configurado).</summary>
    public string?            TelefonoWhatsApp   { get; private set; }

    // Destino de la transferencia (solo se cargan cuando hay un cobro de transferencia a mostrar).
    public string?            TransferenciaAlias   { get; private set; }
    public string?            TransferenciaCvu     { get; private set; }
    public string?            TransferenciaTitular { get; private set; }

    /// <summary>true si hay una forma de pago (QR/link o transferencia) esperando el pago del cliente.</summary>
    public bool EsperandoPago => PagoYaProcesado && Resultado?.Tipo is "mercadopago" or "transferencia";

    [BindProperty]
    public SeleccionPagoDto Seleccion { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        // El cobro (contexto factory) se lee en paralelo con el resumen del pedido
        // (contexto scoped): son lecturas independientes. En los caminos de redirect
        // se espera igual la tarea para observar el resultado y no dejarla colgada.
        var cobroTask = _pagos.ObtenerCobroPorPedidoAsync(Id);

        Pedido = await _pedidos.ObtenerResumenAsync(Id);
        if (Pedido is null)                    { await cobroTask; return RedirectToPage("/Index"); }

        // Si el pedido ya está confirmado, ir a la página de confirmación
        if (Pedido.Estado == "confirmado")     { await cobroTask; return RedirectToPage("/Pedido", new { id = Id }); }

        // Si no está en pendiente_pago, redirigir
        if (Pedido.Estado != "pendiente_pago") { await cobroTask; return RedirectToPage("/Index"); }

        // Ver si ya existe un cobro en proceso (ya se estaba cargando en paralelo)
        CobroExistente = await cobroTask;

        // Reembolso ya registrado → redirigir a la página del pedido
        if (CobroExistente is { Tipo: "reembolso" })
            return RedirectToPage("/Pedido", new { id = Id });

        // MercadoPago pendiente → mostrar QR/link nuevamente
        if (CobroExistente is { Tipo: "mercadopago", Estado: "pendiente" })
        {
            PagoYaProcesado = true;
            Resultado = new ResultadoPagoDto
            {
                Exito             = true,
                PedidoId          = Id,
                CobroId           = CobroExistente.Id,
                Tipo              = "mercadopago",
                MpLinkPago        = CobroExistente.MpLinkPago,
                MpFechaExpiracion = CobroExistente.MpFechaExpiracion
            };
        }

        // Transferencia pendiente y vigente → mostrar de nuevo el destino y el importe exacto.
        // Si ya venció (aunque el proceso de fondo todavía no la haya cancelado), se vuelve a ofrecer
        // la elección de método y, al elegir transferencia otra vez, se genera un cobro nuevo.
        if (CobroExistente is { Tipo: "transferencia", Estado: "pendiente", MontoTransferencia: not null }
            && (CobroExistente.Vencimiento is null || CobroExistente.Vencimiento > DateTime.Now))
        {
            PagoYaProcesado = true;
            Resultado = new ResultadoPagoDto
            {
                Exito              = true,
                PedidoId           = Id,
                CobroId            = CobroExistente.Id,
                Tipo               = "transferencia",
                MontoTransferencia = CobroExistente.MontoTransferencia,
                Vencimiento        = CobroExistente.Vencimiento
            };
        }

        await CargarOpcionesAsync();
        Seleccion.PedidoId = Id;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Pedido = await _pedidos.ObtenerResumenAsync(Id);
            await CargarOpcionesAsync();
            Seleccion.PedidoId = Id;
            return Page();
        }

        var pedidoId = Seleccion.PedidoId;

        // Con ecommerce/LimitarCantidadPorStock = '1' se vuelve a verificar el stock ANTES de generar el cobro:
        // entre el checkout y el pago pasa tiempo (hasta 24 h en transferencias) y el stock pudo cambiar.
        // Solo con el límite activo: apagado, este camino no cambia. Después de cobrar no se valida (el dinero
        // ya entró y no se deja un pedido pagado sin confirmar).
        if (await _params.LimitarCantidadPorStockAsync())
        {
            var (stockOk, erroresStock) = await _pedidos.ValidarStockAsync(pedidoId);
            if (!stockOk)
            {
                foreach (var e in erroresStock)
                    ModelState.AddModelError(string.Empty, e);
                ModelState.AddModelError(string.Empty, "Volvé al carrito para ajustar las cantidades y confirmá de nuevo.");

                Pedido = await _pedidos.ObtenerResumenAsync(pedidoId);
                await CargarOpcionesAsync();
                Seleccion.PedidoId = pedidoId;
                return Page();
            }
        }

        ResultadoPagoDto resultado;

        if (Seleccion.MetodoPago == "reembolso")
        {
            resultado = await _pagos.ProcesarReembolsoAsync(pedidoId);
        }
        else if (Seleccion.MetodoPago == "mercadopago")
        {
            resultado = await _pagos.CrearPreferenciaMercadoPagoAsync(pedidoId);
        }
        else if (Seleccion.MetodoPago == "transferencia")
        {
            resultado = await _pagos.CrearCobroTransferenciaAsync(pedidoId);
        }
        else
        {
            ModelState.AddModelError("Seleccion.MetodoPago", "Método de pago no válido");
            Pedido = await _pedidos.ObtenerResumenAsync(pedidoId);
            await CargarOpcionesAsync();
            Seleccion.PedidoId = pedidoId;
            return Page();
        }

        if (!resultado.Exito)
        {
            ModelState.AddModelError(string.Empty, resultado.Error ?? "Error al procesar el pago");
            Pedido = await _pedidos.ObtenerResumenAsync(pedidoId);
            await CargarOpcionesAsync();
            Seleccion.PedidoId = pedidoId;
            return Page();
        }

        // Reembolso: ya confirmado → ir a confirmación
        if (resultado.Tipo == "reembolso")
        {
            TempData["EmailEnviado"] = resultado.EmailEnviado;
            return RedirectToPage("/Pedido", new { id = pedidoId });
        }

        // MercadoPago / transferencia: mostrar el link de pago o el destino en la misma página
        Resultado         = resultado;
        PagoYaProcesado   = true;
        Pedido            = await _pedidos.ObtenerResumenAsync(pedidoId);
        await CargarOpcionesAsync();
        Seleccion.PedidoId = pedidoId;
        return Page();
    }

    /// <summary>Métodos habilitados, teléfono de la tienda y, si hace falta, el destino de la transferencia.</summary>
    private async Task CargarOpcionesAsync()
    {
        (MetodoReembolso, MetodoMercadoPago, MetodoTransferencia) = await _pagos.ObtenerMetodosHabilitadosAsync();
        TelefonoWhatsApp = await _params.GetValorAsync("empresa", "telefono");

        if (Resultado?.Tipo == "transferencia")
        {
            TransferenciaAlias   = (await _params.GetValorAsync("mercadopago", "transferenciaAlias"))?.Trim();
            TransferenciaCvu     = (await _params.GetValorAsync("mercadopago", "transferenciaCvu"))?.Trim();
            TransferenciaTitular = (await _params.GetValorAsync("mercadopago", "transferenciaTitular"))?.Trim();
        }
    }
}
