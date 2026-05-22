using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

public class CheckoutPageModel : PageModel
{
    private readonly ICarritoService              _carrito;
    private readonly IPedidoService               _pedidos;
    private readonly IDireccionEntregaRepository  _direcciones;
    private readonly IParametrosService           _params;

    public CheckoutPageModel(
        ICarritoService             carrito,
        IPedidoService              pedidos,
        IDireccionEntregaRepository direcciones,
        IParametrosService          parametros)
    {
        _carrito     = carrito;
        _pedidos     = pedidos;
        _direcciones = direcciones;
        _params      = parametros;
    }

    [BindProperty]
    public CheckoutDto Datos { get; set; } = new();

    public List<CarritoItemDto>      Items             { get; private set; } = new();
    public decimal                   SubtotalProductos { get; private set; }
    public List<DireccionEntregaDto> Direcciones       { get; private set; } = new();
    public List<string>              Errores           { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var pedidoId = HttpContext.Session.GetInt32("PedidoId");
        if (!pedidoId.HasValue) return RedirectToPage("/Carrito");

        try
        {
            await _carrito.ActualizarPreciosAsync(pedidoId.Value);
            var (_, errores) = await _pedidos.ValidarStockAsync(pedidoId.Value);
            Errores = errores;

            Items = await _carrito.ObtenerItemsAsync(pedidoId.Value);
            if (!Items.Any()) return RedirectToPage("/Carrito");

            SubtotalProductos = await _carrito.ObtenerTotalAsync(pedidoId.Value);
            Direcciones       = await _direcciones.ObtenerActivasAsync();

            // Pre-seleccionar la dirección por defecto
            var porDefecto = Direcciones.FirstOrDefault(d => d.EsDefault) ?? Direcciones.FirstOrDefault();
            if (porDefecto is not null)
                Datos.DireccionEntregaId = porDefecto.Id;

            // Pre-completar datos del cliente autenticado
            if (User.Identity!.IsAuthenticated)
                RellenarDatosAutenticado();
        }
        catch (InvalidOperationException)
        {
            // El pedido fue eliminado de la BD pero quedó en sesión → limpiar y redirigir
            LimpiarSesionPedido();
            return RedirectToPage("/Carrito");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var pedidoId = HttpContext.Session.GetInt32("PedidoId");
        if (!pedidoId.HasValue) return RedirectToPage("/Carrito");

        // ── Validación condicional por tipo de usuario ───────────────
        if (User.Identity!.IsAuthenticated)
        {
            RellenarDatosAutenticado();
            ModelState.Remove("Datos.NombreCliente");
            ModelState.Remove("Datos.EmailCliente");
            ModelState.Remove("Datos.TelefonoCliente");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Datos.NombreCliente))
                ModelState.AddModelError("Datos.NombreCliente", "El nombre es obligatorio");

            if (string.IsNullOrWhiteSpace(Datos.EmailCliente))
                ModelState.AddModelError("Datos.EmailCliente", "El email es obligatorio");

            if (string.IsNullOrWhiteSpace(Datos.TelefonoCliente))
                ModelState.AddModelError("Datos.TelefonoCliente", "El teléfono es obligatorio");
        }

        try
        {
            // ── Cargar datos para re-render en caso de error ─────────
            Direcciones       = await _direcciones.ObtenerActivasAsync();
            Items             = await _carrito.ObtenerItemsAsync(pedidoId.Value);
            SubtotalProductos = await _carrito.ObtenerTotalAsync(pedidoId.Value);

            // ── Validar dirección libre ───────────────────────────────
            var dirSeleccionada = Direcciones.FirstOrDefault(d => d.Id == Datos.DireccionEntregaId);
            if (dirSeleccionada?.PermiteLibre == true && string.IsNullOrWhiteSpace(Datos.DireccionEntregaTexto))
                ModelState.AddModelError("Datos.DireccionEntregaTexto", "Ingresá la dirección de entrega");

            // ── Validar stock ─────────────────────────────────────────
            await _carrito.ActualizarPreciosAsync(pedidoId.Value);
            var (valido, errores) = await _pedidos.ValidarStockAsync(pedidoId.Value);
            if (!valido) Errores = errores;

            if (!ModelState.IsValid || Errores.Any())
                return Page();

            // ── Confirmar o preparar pago según modo de acceso ────────
            var modo = await _params.GetModoAccesoAsync();
            LimpiarSesionPedido();

            if (modo == "credenciales")
            {
                // Directo: borrador → confirmado sin pasar por /Pago
                var idConfirmado = await _pedidos.ConfirmarDirectoAsync(pedidoId.Value, Datos);
                return RedirectToPage("/Pedido", new { id = idConfirmado });
            }

            var idPreparado = await _pedidos.PrepararPagoAsync(pedidoId.Value, Datos);
            return RedirectToPage("/Pago", new { id = idPreparado });
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("no encontrado") ||
            ex.Message.Contains("no está en estado"))
        {
            // El pedido fue eliminado o está en un estado inválido → limpiar sesión
            LimpiarSesionPedido();
            TempData["Error"] = "Tu sesión de compra expiró o fue cancelada. Por favor, volvé a armar tu carrito.";
            return RedirectToPage("/Carrito");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────
    private void RellenarDatosAutenticado()
    {
        if (int.TryParse(User.FindFirst("clienteId")?.Value, out var cid))
            Datos.ClienteId = cid;
        Datos.NombreCliente = User.FindFirst("nombre")?.Value ?? string.Empty;
        Datos.EmailCliente  = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? string.Empty;
    }

    private void LimpiarSesionPedido()
    {
        HttpContext.Session.Remove("PedidoId");
    }
}
