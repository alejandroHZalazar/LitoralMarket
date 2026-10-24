using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace LitoralMarket.Web.Pages.Admin.Rubros;

[Authorize(Roles = "admin")]
public class IndexModel : PageModel
{
    private readonly IRubroAdminService _rubros;
    private readonly IProductoRepository _productos;

    public IndexModel(IRubroAdminService rubros, IProductoRepository productos)
    {
        _rubros    = rubros;
        _productos = productos;
    }

    [BindProperty]
    public RubroForm Rubro { get; set; } = new();

    public class RubroForm
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "La descripción es obligatoria."), MaxLength(150)]
        public string? Descripcion { get; set; }

        /// <summary>Rubro padre. null/0 = rubro principal.</summary>
        public int? PadreId { get; set; }
    }

    // ── GET ──────────────────────────────────────────────────────
    public void OnGet() { }

    // ── AJAX: buscar (filas HTML, en orden de árbol y con sangría por nivel) ──
    public async Task<IActionResult> OnGetBuscarAsync(string valor)
    {
        valor ??= string.Empty;
        var lista = await _rubros.BuscarAsync(valor);

        var sb = new System.Text.StringBuilder();
        foreach (var r in lista)
        {
            var descEnc = System.Net.WebUtility.HtmlEncode(r.Descripcion);
            var rutaEnc = System.Net.WebUtility.HtmlEncode(r.Ruta);
            var descJs  = System.Net.WebUtility.HtmlEncode(r.Descripcion.Replace("\\", "\\\\").Replace("'", "\\'"));
            var sangria = (r.Nivel - 1) * 1.5;
            var icono   = r.Nivel > 1 ? "bi-arrow-return-right text-muted me-1" : "bi-folder2 text-success me-1";
            var padre   = r.PadreId ?? 0;
            var sinProductos = r.CantProductos == 0;

            sb.Append($@"<tr class=""fila-rubro"" data-id=""{r.Id}"">
  <td title=""{rutaEnc}"" style=""padding-left:{sangria.ToString(System.Globalization.CultureInfo.InvariantCulture)}rem"">
    <i class=""bi {icono}""></i>{descEnc}
  </td>
  <td class=""text-center"">
    <span class=""badge bg-secondary"">{r.CantProductos}</span>
  </td>
  <td class=""text-end text-nowrap"">
    <button class=""btn btn-sm btn-outline-primary me-1"" onclick=""abrirModalEditar({r.Id},'{descJs}',{padre});event.stopPropagation();"" title=""Editar"">
      <i class=""bi bi-pencil""></i>
    </button>
    <button class=""btn btn-sm btn-outline-danger"" onclick=""confirmarEliminar({r.Id},'{descJs}');event.stopPropagation();"" title=""{(sinProductos ? "Eliminar" : "Tiene productos activos")}"" {(sinProductos ? "" : "disabled")}>
      <i class=""bi bi-trash""></i>
    </button>
  </td>
</tr>");
        }

        if (!lista.Any())
            sb.Append(@"<tr><td colspan=""3"" class=""text-center text-muted py-3"">Sin resultados</td></tr>");

        return Content(sb.ToString(), "text/html");
    }

    // ── AJAX: árbol completo para el combo "Rubro padre" del modal ──
    public async Task<IActionResult> OnGetArbolAsync()
    {
        var arbol = await _productos.ObtenerArbolRubrosAsync();
        return new JsonResult(arbol.Select(n => new { id = n.Id, ruta = n.Ruta, padreId = n.PadreId }));
    }

    // ── POST: guardar ─────────────────────────────────────────────
    public async Task<IActionResult> OnPostGuardarAsync()
    {
        if (!ModelState.IsValid)
            return new JsonResult(new { ok = false, error = "La descripción es obligatoria (máximo 150 caracteres)." });

        var padre = Rubro.PadreId is > 0 ? Rubro.PadreId : null;
        var (ok, error) = await _rubros.GuardarAsync(Rubro.Id, Rubro.Descripcion!, padre);
        return new JsonResult(new { ok, error });
    }

    // ── POST: eliminar ────────────────────────────────────────────
    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        var (ok, error) = await _rubros.EliminarAsync(id);
        return new JsonResult(new { ok, error });
    }
}
