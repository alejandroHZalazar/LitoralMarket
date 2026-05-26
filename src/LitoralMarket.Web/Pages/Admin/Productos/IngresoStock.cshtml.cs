using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages.Admin.Productos;

[Authorize(Roles = "admin")]
public class IngresoStockModel : PageModel
{
    private readonly IProductoAdminService _productos;

    public IngresoStockModel(IProductoAdminService productos) => _productos = productos;

    // ── GET: carga inicial ───────────────────────────────────────
    public void OnGet() { }

    // ── AJAX: buscar productos ───────────────────────────────────
    public async Task<IActionResult> OnGetBuscarAsync(int tipo, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return Content(FilaVacia("Escribí al menos un carácter para buscar."), "text/html");

        var lista = await _productos.BuscarAsync(tipo, valor.Trim());

        if (!lista.Any())
            return Content(FilaVacia("No se encontraron productos."), "text/html");

        var filas = string.Concat(lista.Select(p =>
            $"<tr class=\"fila-resultado\" data-id=\"{p.Id}\" tabindex=\"-1\">" +
            $"<td class=\"ps-2 small font-monospace\">{Enc(p.CodProveedor)}</td>" +
            $"<td class=\"small font-monospace\">{Enc(p.CodBarras)}</td>" +
            $"<td class=\"small\">{Enc(p.Descripcion)}</td>" +
            $"</tr>"));

        return Content(filas, "text/html");
    }

    // ── AJAX: datos del producto seleccionado ────────────────────
    public async Task<IActionResult> OnGetProductoAsync(int id)
    {
        var p = await _productos.ObtenerPorIdAsync(id);
        if (p is null)
            return new JsonResult(new { ok = false, error = "Producto no encontrado." });

        return new JsonResult(new
        {
            ok           = true,
            id           = p.Id,
            descripcion  = p.Descripcion,
            codProveedor = p.CodProveedor,
            codBarras    = p.CodBarras,
            stockActual  = (int)Math.Floor(p.StockActual)
        });
    }

    // ── POST masivo (JSON body + token en header) ────────────────
    public async Task<IActionResult> OnPostConfirmarMasivoAsync(
        [FromBody] ConfirmarMasivoRequest req)
    {
        if (req?.Items is not { Count: > 0 })
            return new JsonResult(new { ok = false, error = "No hay productos para ingresar." });

        if (req.Items.Any(i => i.Cantidad <= 0))
            return new JsonResult(new { ok = false, error = "Todas las cantidades deben ser mayores a cero." });

        try
        {
            var items = req.Items.Select(i =>
                new IngresoItemRequest(i.ProductoId, i.Cantidad, i.Observacion));
            await _productos.IngresoStockMasivoAsync(items);
            return new JsonResult(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message });
        }
        catch
        {
            return new JsonResult(new
            {
                ok    = false,
                error = "Error inesperado al registrar el ingreso. No se realizaron cambios."
            });
        }
    }

    // ── Helpers ──────────────────────────────────────────────────
    private static string Enc(string? s)
        => string.IsNullOrEmpty(s)
            ? "<span class=\"text-muted\">—</span>"
            : System.Net.WebUtility.HtmlEncode(s);

    private static string FilaVacia(string msg)
        => $"<tr><td colspan=\"3\" class=\"text-center text-muted py-3 small\">{msg}</td></tr>";

    // ── DTOs locales (capa HTTP) ─────────────────────────────────
    public record ConfirmarMasivoRequest(List<IngresoItemDto> Items);
    public record IngresoItemDto(int ProductoId, int Cantidad, string? Observacion);
}
