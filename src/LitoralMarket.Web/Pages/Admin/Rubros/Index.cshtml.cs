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
    public IndexModel(IRubroAdminService rubros) => _rubros = rubros;

    [BindProperty]
    public RubroForm Rubro { get; set; } = new();

    public class RubroForm
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "La descripción es obligatoria."), MaxLength(200)]
        public string? Descripcion { get; set; }
    }

    // ── GET ──────────────────────────────────────────────────────
    public void OnGet() { }

    // ── AJAX: buscar (filas HTML) ─────────────────────────────────
    public async Task<IActionResult> OnGetBuscarAsync(string valor)
    {
        valor ??= string.Empty;
        var lista = await _rubros.BuscarAsync(valor);

        var sb = new System.Text.StringBuilder();
        foreach (var (id, desc, cant) in lista)
        {
            var descEnc = System.Net.WebUtility.HtmlEncode(desc);
            var descJs  = desc.Replace("'", "\\'");
            sb.Append($@"<tr class=""fila-rubro"" data-id=""{id}"" role=""button"">
  <td>{descEnc}</td>
  <td class=""text-center"">
    <span class=""badge bg-secondary"">{cant}</span>
  </td>
  <td class=""text-end text-nowrap"">
    <button class=""btn btn-sm btn-outline-primary me-1"" onclick=""abrirModalEditar({id},'{descJs}');event.stopPropagation();"" title=""Editar"">
      <i class=""bi bi-pencil""></i>
    </button>
    <button class=""btn btn-sm btn-outline-danger"" onclick=""confirmarEliminar({id},'{descJs}',{cant});event.stopPropagation();"" title=""Eliminar"" {(cant > 0 ? "disabled title=\"Tiene productos activos\"" : "")}>
      <i class=""bi bi-trash""></i>
    </button>
  </td>
</tr>");
        }

        if (!lista.Any())
            sb.Append(@"<tr><td colspan=""3"" class=""text-center text-muted py-3"">Sin resultados</td></tr>");

        return Content(sb.ToString(), "text/html");
    }

    // ── POST: guardar ─────────────────────────────────────────────
    public async Task<IActionResult> OnPostGuardarAsync()
    {
        if (!ModelState.IsValid)
            return new JsonResult(new { ok = false, error = "La descripción es obligatoria." });

        if (Rubro.Id == 0)
            await _rubros.CrearAsync(Rubro.Descripcion!);
        else
            await _rubros.ActualizarAsync(Rubro.Id, Rubro.Descripcion!);

        return new JsonResult(new { ok = true });
    }

    // ── POST: eliminar ────────────────────────────────────────────
    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        var eliminado = await _rubros.EliminarAsync(id);
        return new JsonResult(new { ok = eliminado, error = eliminado ? null : "El rubro tiene productos activos y no puede eliminarse." });
    }
}
