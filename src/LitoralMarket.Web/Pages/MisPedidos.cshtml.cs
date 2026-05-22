using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

// Sin [Authorize] — acceso libre; la lógica interna bifurca entre autenticado y anónimo
public class MisPedidosModel : PageModel
{
    private readonly IPedidoService    _pedidos;
    private readonly IParametrosService _params;

    public MisPedidosModel(IPedidoService pedidos, IParametrosService parametros)
    {
        _pedidos = pedidos;
        _params  = parametros;
    }

    public List<PedidoResumenDto> Pedidos            { get; private set; } = new();
    public bool                   EsAnonimo          { get; private set; }
    public bool                   EsModoCredenciales { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        EsModoCredenciales = await _params.GetModoAccesoAsync() == "credenciales";

        if (User.Identity?.IsAuthenticated ?? false)
        {
            var clienteIdClaim = User.FindFirst("clienteId");
            if (clienteIdClaim is not null && int.TryParse(clienteIdClaim.Value, out var clienteId))
                Pedidos = await _pedidos.ObtenerPorClienteAsync(clienteId);

            return Page();
        }

        // Usuario anónimo: buscar por cookie litoral_guest
        EsAnonimo = true;

        if (Request.Cookies.TryGetValue("litoral_guest", out var guestToken)
            && !string.IsNullOrEmpty(guestToken))
        {
            Pedidos = await _pedidos.ObtenerPorGuestTokenAsync(guestToken);
        }

        return Page();
    }
}
