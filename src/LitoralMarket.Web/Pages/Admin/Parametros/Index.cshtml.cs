using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace LitoralMarket.Web.Pages.Admin.Parametros;

[Authorize(Roles = "admin")]
public class IndexModel : PageModel
{
    private readonly IParametrosAdminService _svc;
    public IndexModel(IParametrosAdminService svc) => _svc = svc;

    public List<Parametro> Parametros { get; private set; } = new();

    // ── GET — carga la página ─────────────────────────────────────────────
    public async Task OnGetAsync() =>
        Parametros = await _svc.ListarAsync();

    // ── GET AJAX — devuelve datos del parámetro para el modal ─────────────
    public async Task<IActionResult> OnGetParametroJsonAsync(int id)
    {
        var p = await _svc.ObtenerPorIdAsync(id);
        if (p is null) return NotFound();

        return new JsonResult(new
        {
            id              = p.Id,
            modulo          = p.Modulo,
            parametroNombre = p.ParametroNombre,
            valor           = p.Valor,
            tieneImagen     = p.Imagen is { Length: > 0 }
        });
    }

    // ── GET AJAX — sirve la imagen de un parámetro ───────────────────────
    public async Task<IActionResult> OnGetImagenAsync(int id)
    {
        var p = await _svc.ObtenerPorIdAsync(id);
        if (p?.Imagen is null or { Length: 0 }) return NotFound();

        var img = p.Imagen;
        string mime = img.Length >= 2 && img[0] == 0xFF && img[1] == 0xD8 ? "image/jpeg"
                    : img.Length >= 4 && img[0] == 0x89 && img[1] == 0x50 ? "image/png"
                    : img.Length >= 3 && img[0] == 0x47 && img[1] == 0x49 ? "image/gif"
                    : "image/webp";

        return File(img, mime);
    }

    // ── POST — guardar (crear o actualizar) ───────────────────────────────
    public async Task<IActionResult> OnPostGuardarAsync()
    {
        if (!int.TryParse(Request.Form["Id"], out var id))
            return new JsonResult(new { ok = false, error = "Id inválido." });

        var modulo  = Request.Form["Modulo"].ToString().Trim();
        var nombre  = Request.Form["ParametroNombre"].ToString().Trim();
        var valor   = Request.Form["Valor"].ToString();
        var quitarImagen = Request.Form["QuitarImagen"] == "true";

        if (string.IsNullOrWhiteSpace(modulo) || string.IsNullOrWhiteSpace(nombre))
            return new JsonResult(new { ok = false, error = "Módulo y parámetro son obligatorios." });

        byte[]? imagen = null;
        if (Request.Form.Files.GetFile("Imagen") is { Length: > 0 } file)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            imagen = ms.ToArray();
        }

        if (id == 0)
        {
            await _svc.CrearAsync(new Parametro
            {
                Modulo          = modulo,
                ParametroNombre = nombre,
                Valor           = valor,
                Imagen          = imagen
            });
        }
        else
        {
            await _svc.ActualizarAsync(id, modulo, nombre, valor, imagen, quitarImagen);
        }

        return new JsonResult(new { ok = true });
    }

    // ── POST — eliminar ───────────────────────────────────────────────────
    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        await _svc.EliminarAsync(id);
        return new JsonResult(new { ok = true });
    }
}
