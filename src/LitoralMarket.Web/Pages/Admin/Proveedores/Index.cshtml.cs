using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;
using System.Text.Json;

namespace LitoralMarket.Web.Pages.Admin.Proveedores;

[Authorize(Roles = "admin")]
public class IndexModel : PageModel
{
    private readonly IProveedorAdminService _proveedores;
    public IndexModel(IProveedorAdminService proveedores) => _proveedores = proveedores;

    [BindProperty] public ProveedorAdminDto Proveedor { get; set; } = new();

    // ── GET principal ────────────────────────────────────────────
    public void OnGet() { }

    // ── AJAX: buscar (devuelve filas HTML) ───────────────────────
    public async Task<IActionResult> OnGetBuscarAsync(string valor)
    {
        valor ??= string.Empty;
        var lista = await _proveedores.BuscarAsync(valor);

        var sb = new System.Text.StringBuilder();
        foreach (var p in lista)
        {
            var nombre   = System.Net.WebUtility.HtmlEncode(p.NombreComercial ?? "-");
            var cuil     = System.Net.WebUtility.HtmlEncode(p.Cuil     ?? "-");
            var email    = System.Net.WebUtility.HtmlEncode(p.Email    ?? "-");
            var telefono = System.Net.WebUtility.HtmlEncode(p.Telefono ?? p.Celular ?? "-");

            sb.Append($@"<tr class=""fila-proveedor"" data-id=""{p.Id}"" role=""button"">
  <td>{nombre}</td>
  <td class=""text-muted small"">{cuil}</td>
  <td class=""text-muted small"">{email}</td>
  <td class=""text-muted small"">{telefono}</td>
  <td class=""text-end text-nowrap"">
    <button class=""btn btn-sm btn-outline-primary me-1"" onclick=""abrirModalEditar({p.Id});event.stopPropagation();"" title=""Editar"">
      <i class=""bi bi-pencil""></i>
    </button>
    <button class=""btn btn-sm btn-outline-danger"" onclick=""confirmarEliminar({p.Id},'{p.NombreComercial?.Replace("'", "\\'") ?? ""}');event.stopPropagation();"" title=""Dar de baja"">
      <i class=""bi bi-trash""></i>
    </button>
  </td>
</tr>");
        }

        if (!lista.Any())
            sb.Append(@"<tr><td colspan=""5"" class=""text-center text-muted py-3"">Sin resultados</td></tr>");

        return Content(sb.ToString(), "text/html");
    }

    // ── AJAX: detalle HTML ───────────────────────────────────────
    public async Task<IActionResult> OnGetDetalleAsync(int id)
    {
        var p = await _proveedores.ObtenerPorIdAsync(id);
        if (p is null) return Content("<p class=\"text-danger\">Proveedor no encontrado.</p>", "text/html");

        var html = $@"
<div class=""p-2"">
  <h6 class=""fw-bold mb-1"">{System.Net.WebUtility.HtmlEncode(p.NombreComercial ?? "-")}</h6>
  <p class=""text-muted small mb-2"">CUIL: {System.Net.WebUtility.HtmlEncode(p.Cuil ?? "-")}</p>
  <hr class=""my-2""/>
  <div class=""row g-1 small"">
    <div class=""col-12""><span class=""text-muted"">Dirección:</span> <strong>{System.Net.WebUtility.HtmlEncode(p.Direccion ?? "-")}</strong></div>
    <div class=""col-12""><span class=""text-muted"">Email:</span> <strong>{System.Net.WebUtility.HtmlEncode(p.Email ?? "-")}</strong></div>
    <div class=""col-6""><span class=""text-muted"">Teléfono:</span> <strong>{System.Net.WebUtility.HtmlEncode(p.Telefono ?? "-")}</strong></div>
    <div class=""col-6""><span class=""text-muted"">Celular:</span> <strong>{System.Net.WebUtility.HtmlEncode(p.Celular ?? "-")}</strong></div>
  </div>
  <hr class=""my-2""/>
  <div class=""row g-1 small"">
    <div class=""col-6"">
      <span class=""text-muted"">Ganancia:</span>
      <strong class=""text-success"">{p.Ganancia:N2}%</strong>
    </div>
    <div class=""col-6"">
      <span class=""text-muted"">Descuento:</span>
      <strong class=""text-danger"">{p.Descuento:N2}%</strong>
    </div>
  </div>
</div>";

        return Content(html, "text/html");
    }

    // ── AJAX: JSON para el modal ─────────────────────────────────
    public async Task<IActionResult> OnGetProveedorJsonAsync(int id)
    {
        var p = await _proveedores.ObtenerPorIdAsync(id);
        if (p is null) return NotFound();

        return new JsonResult(p, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    // ── POST: guardar (alta o modificación) ──────────────────────
    public async Task<IActionResult> OnPostGuardarAsync()
    {
        static decimal Parse(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
            if (decimal.TryParse(s.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        Proveedor.Ganancia  = Parse(Request.Form["Proveedor.Ganancia"]);
        Proveedor.Descuento = Parse(Request.Form["Proveedor.Descuento"]);

        ModelState.Remove(nameof(Proveedor) + "." + nameof(Proveedor.Ganancia));
        ModelState.Remove(nameof(Proveedor) + "." + nameof(Proveedor.Descuento));

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Revisá los datos del formulario.";
            return Page();
        }

        if (Proveedor.Id == 0)
            await _proveedores.CrearAsync(Proveedor);
        else
            await _proveedores.ActualizarAsync(Proveedor);

        TempData["Mensaje"] = Proveedor.Id == 0
            ? "Proveedor creado correctamente."
            : "Proveedor actualizado correctamente.";

        return RedirectToPage();
    }

    // ── POST: baja lógica ────────────────────────────────────────
    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        await _proveedores.BajaLogicaAsync(id);
        return new JsonResult(new { ok = true });
    }
}
