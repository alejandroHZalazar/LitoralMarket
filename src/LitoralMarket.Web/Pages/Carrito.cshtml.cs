using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

public class CarritoModel : PageModel
{
    private readonly ICarritoService _carrito;
    private readonly IParametrosService _parametros;

    public CarritoModel(ICarritoService carrito, IParametrosService parametros)
    {
        _carrito = carrito;
        _parametros = parametros;
    }

    public List<CarritoItemDto> Items         { get; private set; } = new();
    public List<string>         Eliminados    { get; private set; } = new();
    public decimal              Total         { get; private set; }

    private async Task<int> ObtenerPedidoIdAsync()
    {
        var clienteId = ObtenerClienteId();
        var guestToken = ObtenerOCrearGuestToken();
        var pedidoId = HttpContext.Session.GetInt32("PedidoId");

        if (!pedidoId.HasValue)
        {
            pedidoId = await _carrito.ObtenerOCrearBorradorAsync(guestToken, clienteId);
            HttpContext.Session.SetInt32("PedidoId", pedidoId.Value);
        }

        return pedidoId.Value;
    }

    private int? ObtenerClienteId()
    {
        var claim = User.FindFirst("clienteId");
        return claim is not null && int.TryParse(claim.Value, out var id) ? id : null;
    }

    private string ObtenerOCrearGuestToken()
    {
        const string cookieName = "litoral_guest";
        if (Request.Cookies.TryGetValue(cookieName, out var token) && !string.IsNullOrEmpty(token))
            return token;

        token = Guid.NewGuid().ToString();
        Response.Cookies.Append(cookieName, token, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            IsEssential = true
        });
        return token;
    }

    public async Task OnGetAsync()
    {
        var pedidoId = await ObtenerPedidoIdAsync();

        // Sincronizar: refresca precios/costos y elimina sin stock (ya persiste el total)
        Eliminados = await _carrito.SincronizarCarritoAsync(pedidoId);

        Items = await _carrito.ObtenerItemsAsync(pedidoId);
        // El total se calcula en memoria desde los items ya cargados: es la misma
        // suma de subtotales que el sync acaba de persistir, sin round-trips extra.
        Total = Items.Sum(i => i.Subtotal);
        ViewData["CarritoCount"] = Items.Count;
    }

    // POST estándar (fallback sin JS)
    public async Task<IActionResult> OnPostAgregarAsync(int productoId, decimal cantidad = 1)
    {
        var pedidoId = await ObtenerPedidoIdAsync();
        await _carrito.AgregarItemAsync(pedidoId, productoId, cantidad);
        TempData["Mensaje"] = "Producto agregado al carrito";
        return RedirectToPage("/Index");
    }

    // POST vía AJAX — devuelve JSON
    public async Task<IActionResult> OnPostAgregarAjaxAsync(int productoId, decimal cantidad = 1)
    {
        try
        {
            var pedidoId = await ObtenerPedidoIdAsync();
            await _carrito.AgregarItemAsync(pedidoId, productoId, cantidad);
            var items = await _carrito.ObtenerItemsAsync(pedidoId);
            return new JsonResult(new { ok = true, cantidadCarrito = items.Count });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { ok = false, mensaje = ex.Message })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    public async Task<IActionResult> OnPostQuitarAsync(long lineaId)
    {
        var pedidoId = HttpContext.Session.GetInt32("PedidoId");
        if (pedidoId.HasValue)
            await _carrito.QuitarItemAsync(pedidoId.Value, lineaId);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostActualizarCantidadAsync(long lineaId, decimal cantidad)
    {
        var pedidoId = HttpContext.Session.GetInt32("PedidoId");
        if (pedidoId.HasValue)
            await _carrito.ActualizarCantidadAsync(pedidoId.Value, lineaId, cantidad);
        return RedirectToPage();
    }
}
