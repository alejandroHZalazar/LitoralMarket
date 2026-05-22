using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

public class PagoPageModel : PageModel
{
    private readonly IPagoEcommerceService _pagos;
    private readonly IPedidoService        _pedidos;

    public PagoPageModel(IPagoEcommerceService pagos, IPedidoService pedidos)
    {
        _pagos   = pagos;
        _pedidos = pedidos;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public PedidoResumenDto? Pedido              { get; private set; }
    public ResultadoPagoDto? Resultado           { get; private set; }
    public bool              MetodoReembolso     { get; private set; }
    public bool              MetodoMercadoPago   { get; private set; }
    public bool              PagoYaProcesado     { get; private set; }
    public CobroEcommerceDto? CobroExistente     { get; private set; }

    [BindProperty]
    public SeleccionPagoDto Seleccion { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        Pedido = await _pedidos.ObtenerResumenAsync(Id);
        if (Pedido is null) return RedirectToPage("/Index");

        // Si el pedido ya está confirmado, ir a la página de confirmación
        if (Pedido.Estado == "confirmado")
            return RedirectToPage("/Pedido", new { id = Id });

        // Si no está en pendiente_pago, redirigir
        if (Pedido.Estado != "pendiente_pago")
            return RedirectToPage("/Index");

        // Ver si ya existe un cobro en proceso
        CobroExistente = await _pagos.ObtenerCobroPorPedidoAsync(Id);

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

        (MetodoReembolso, MetodoMercadoPago) = await _pagos.ObtenerMetodosHabilitadosAsync();
        Seleccion.PedidoId = Id;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Pedido = await _pedidos.ObtenerResumenAsync(Id);
            (MetodoReembolso, MetodoMercadoPago) = await _pagos.ObtenerMetodosHabilitadosAsync();
            Seleccion.PedidoId = Id;
            return Page();
        }

        var pedidoId = Seleccion.PedidoId;

        ResultadoPagoDto resultado;

        if (Seleccion.MetodoPago == "reembolso")
        {
            resultado = await _pagos.ProcesarReembolsoAsync(pedidoId);
        }
        else if (Seleccion.MetodoPago == "mercadopago")
        {
            resultado = await _pagos.CrearPreferenciaMercadoPagoAsync(pedidoId);
        }
        else
        {
            ModelState.AddModelError("Seleccion.MetodoPago", "Método de pago no válido");
            Pedido = await _pedidos.ObtenerResumenAsync(pedidoId);
            (MetodoReembolso, MetodoMercadoPago) = await _pagos.ObtenerMetodosHabilitadosAsync();
            Seleccion.PedidoId = pedidoId;
            return Page();
        }

        if (!resultado.Exito)
        {
            ModelState.AddModelError(string.Empty, resultado.Error ?? "Error al procesar el pago");
            Pedido = await _pedidos.ObtenerResumenAsync(pedidoId);
            (MetodoReembolso, MetodoMercadoPago) = await _pagos.ObtenerMetodosHabilitadosAsync();
            Seleccion.PedidoId = pedidoId;
            return Page();
        }

        // Reembolso: ya confirmado → ir a confirmación
        if (resultado.Tipo == "reembolso")
        {
            TempData["EmailEnviado"] = resultado.EmailEnviado;
            return RedirectToPage("/Pedido", new { id = pedidoId });
        }

        // MercadoPago: mostrar link de pago en la misma página
        Resultado         = resultado;
        PagoYaProcesado   = true;
        Pedido            = await _pedidos.ObtenerResumenAsync(pedidoId);
        (MetodoReembolso, MetodoMercadoPago) = await _pagos.ObtenerMetodosHabilitadosAsync();
        Seleccion.PedidoId = pedidoId;
        return Page();
    }
}
