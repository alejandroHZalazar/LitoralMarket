using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

/// <summary>
/// Página de retorno desde MercadoPago (back_urls).
/// </summary>
public class PagoResultadoModel : PageModel
{
    private readonly IPedidoService _pedidos;

    public PagoResultadoModel(IPedidoService pedidos) => _pedidos = pedidos;

    public int PedidoId  { get; private set; }
    public string Resultado { get; private set; } = string.Empty;
    public bool Aprobado    { get; private set; }

    public async Task<IActionResult> OnGetAsync(int pedidoId, string resultado)
    {
        PedidoId  = pedidoId;
        Resultado = resultado;
        Aprobado  = resultado == "ok";

        // Si fue aprobado y el pedido ya está confirmado (webhook ya procesó), ir al resumen
        if (Aprobado)
        {
            var pedido = await _pedidos.ObtenerResumenAsync(pedidoId);
            if (pedido?.Estado == "confirmado")
                return RedirectToPage("/Pedido", new { id = pedidoId });
        }

        return Page();
    }
}
