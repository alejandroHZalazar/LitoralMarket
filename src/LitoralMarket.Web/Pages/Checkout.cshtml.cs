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
            // Direcciones es independiente del pedido → arranca ya, en paralelo,
            // sobre su propio contexto (factory). Se superpone con el resto del trabajo.
            var direccionesTask = _direcciones.ObtenerActivasAsync();

            // Mutación: refresca y persiste precios/costos (contexto scoped). Debe
            // completar antes de leer items y validar stock (dependen de esos precios).
            await _carrito.ActualizarPreciosAsync(pedidoId.Value);

            // Lecturas independientes en paralelo. Solo ObtenerItems usa el contexto
            // scoped (único que puede); ValidarStock y Direcciones usan contextos
            // propios de la factory → sin colisión ni condiciones de carrera.
            var itemsTask = _carrito.ObtenerItemsAsync(pedidoId.Value);
            var stockTask = _pedidos.ValidarStockAsync(pedidoId.Value);
            await Task.WhenAll(itemsTask, stockTask, direccionesTask);

            Items       = itemsTask.Result;
            Errores     = stockTask.Result.errores;
            Direcciones = direccionesTask.Result;

            if (!Items.Any()) return RedirectToPage("/Carrito");

            // ActualizarPreciosAsync ya refrescó y persistió los subtotales; el total
            // se calcula en memoria desde los items ya cargados (sin round-trips extra).
            SubtotalProductos = Items.Sum(i => i.Subtotal);

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
            // ── Validaciones críticas en paralelo (independientes entre sí) ──
            //  • ObtenerActivas (direcciones) y ValidarStock → contextos propios (factory)
            //  • ActualizarPrecios (mutación de precios) → contexto scoped
            // Solo una toca el scoped → sin condiciones de carrera. ValidarStock lee
            // 'cantidad'/'stock' y ActualizarPrecios escribe 'precio'/'subtotal':
            // columnas disjuntas, así que el resultado de la validación es idéntico.
            var direccionesTask = _direcciones.ObtenerActivasAsync();
            var stockTask       = _pedidos.ValidarStockAsync(pedidoId.Value);
            await _carrito.ActualizarPreciosAsync(pedidoId.Value);

            Direcciones = await direccionesTask;
            var (valido, errores) = await stockTask;
            if (!valido) Errores = errores;

            // ── Validar dirección libre (requiere Direcciones) ────────
            var dirSeleccionada = Direcciones.FirstOrDefault(d => d.Id == Datos.DireccionEntregaId);
            if (dirSeleccionada?.PermiteLibre == true && string.IsNullOrWhiteSpace(Datos.DireccionEntregaTexto))
                ModelState.AddModelError("Datos.DireccionEntregaTexto", "Ingresá la dirección de entrega");

            if (!ModelState.IsValid || Errores.Any())
            {
                // Los items solo se necesitan para RE-RENDERIZAR la página en caso de
                // error. En el camino feliz se redirige a /Pago sin usarlos → cargarlos
                // acá evita 2 round-trips (ObtenerItems + ObtenerTotal) en cada confirmación.
                Items             = await _carrito.ObtenerItemsAsync(pedidoId.Value);
                SubtotalProductos = Items.Sum(i => i.Subtotal);
                return Page();
            }

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
