using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;

namespace LitoralMarket.Web.Pages.Admin.DireccionesEntrega;

[Authorize(Roles = "admin")]
public class IndexModel : PageModel
{
    private readonly IDireccionEntregaAdminService _svc;
    public IndexModel(IDireccionEntregaAdminService svc) => _svc = svc;

    public List<DireccionEntrega> Direcciones { get; private set; } = new();

    [BindProperty] public DireccionForm Direccion { get; set; } = new();

    public class DireccionForm
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "La descripción es obligatoria."), MaxLength(200)]
        public string Descripcion { get; set; } = string.Empty;

        public string TipoEntrega  { get; set; } = "retiro";
        public string? Direccion_  { get; set; }   // alias: Razor no acepta "Direccion.Direccion"
        public string? Localidad   { get; set; }
        public string? Provincia   { get; set; }
        public string? CodigoPostal{ get; set; }
        public string? Referencia  { get; set; }
        public decimal CostoEnvio  { get; set; }
        public bool    EsGratis    { get; set; }
        public bool    PermiteLibre{ get; set; }
        public bool    EsDefault   { get; set; }
        public bool    Activo      { get; set; } = true;
    }

    // ── GET ──────────────────────────────────────────────────────
    public async Task OnGetAsync() =>
        Direcciones = await _svc.ListarAsync();

    // ── AJAX: JSON para el modal ─────────────────────────────────
    public async Task<IActionResult> OnGetDireccionJsonAsync(int id)
    {
        var d = await _svc.ObtenerPorIdAsync(id);
        if (d is null) return NotFound();
        return new JsonResult(d, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    // ── POST: guardar ─────────────────────────────────────────────
    public async Task<IActionResult> OnPostGuardarAsync()
    {
        static decimal Parse(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
            if (decimal.TryParse(s.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        Direccion.CostoEnvio = Parse(Request.Form["Direccion.CostoEnvio"]);
        ModelState.Remove("Direccion.CostoEnvio");

        if (!ModelState.IsValid)
            return new JsonResult(new { ok = false, error = "Revisá los datos del formulario." });

        var entidad = new DireccionEntrega
        {
            Id          = Direccion.Id,
            Descripcion = Direccion.Descripcion,
            TipoEntrega = Direccion.TipoEntrega,
            Direccion   = Direccion.Direccion_,
            Localidad   = Direccion.Localidad,
            Provincia   = Direccion.Provincia,
            CodigoPostal= Direccion.CodigoPostal,
            Referencia  = Direccion.Referencia,
            CostoEnvio  = Direccion.CostoEnvio,
            EsGratis    = Direccion.EsGratis,
            PermiteLibre= Direccion.PermiteLibre,
            EsDefault   = Direccion.EsDefault,
            Activo      = Direccion.Activo
        };

        if (Direccion.Id == 0)
            await _svc.CrearAsync(entidad);
        else
            await _svc.ActualizarAsync(entidad);

        return new JsonResult(new { ok = true });
    }

    // ── POST: activar / desactivar ────────────────────────────────
    public async Task<IActionResult> OnPostToggleActivoAsync(int id)
    {
        await _svc.ToggleActivoAsync(id);
        return new JsonResult(new { ok = true });
    }

    // ── POST: eliminar ────────────────────────────────────────────
    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        await _svc.EliminarAsync(id);
        return new JsonResult(new { ok = true });
    }

    // ── POST: reordenar ───────────────────────────────────────────
    public async Task<IActionResult> OnPostMoverAsync(int id, bool subir)
    {
        await _svc.MoverAsync(id, subir);
        return new JsonResult(new { ok = true });
    }
}
