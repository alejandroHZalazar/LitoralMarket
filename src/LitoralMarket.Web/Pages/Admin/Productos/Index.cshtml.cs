using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Globalization;
using System.Text.Json;

namespace LitoralMarket.Web.Pages.Admin.Productos;

[Authorize(Roles = "admin")]
[RequestSizeLimit(20 * 1024 * 1024)]
public class IndexModel : PageModel
{
    private readonly IProductoAdminService _productos;
    private readonly IProveedorRepository  _proveedores;
    private readonly IProductoRepository   _rubroRepo;

    public IndexModel(
        IProductoAdminService productos,
        IProveedorRepository  proveedores,
        IProductoRepository   rubroRepo)
    {
        _productos   = productos;
        _proveedores = proveedores;
        _rubroRepo   = rubroRepo;
    }

    // ── Estado de la página ──────────────────────────────────────
    public List<SelectListItem>  Rubros      { get; private set; } = new();
    public List<ProveedorDto>    Proveedores { get; private set; } = new();

    [BindProperty] public ProductoAdminDto Producto { get; set; } = new();

    // ── GET principal ────────────────────────────────────────────
    public async Task OnGetAsync()
    {
        await CargarDropdownsAsync();
    }

    // ── AJAX: buscar productos (devuelve filas HTML) ─────────────
    public async Task<IActionResult> OnGetBuscarProductosAsync(int tipo, string valor)
    {
        valor ??= string.Empty;
        var lista = await _productos.BuscarAsync(tipo, valor);

        var sb = new System.Text.StringBuilder();
        foreach (var p in lista)
        {
            var desc  = System.Net.WebUtility.HtmlEncode(p.Descripcion ?? "-");
            var codP  = System.Net.WebUtility.HtmlEncode(p.CodProveedor ?? "-");
            var codB  = System.Net.WebUtility.HtmlEncode(p.CodBarras ?? "-");
            var rubro = System.Net.WebUtility.HtmlEncode(p.RubroNombre ?? "-");

            sb.Append($@"<tr class=""fila-producto"" data-id=""{p.Id}"" role=""button"">
  <td>{codP}</td>
  <td>{codB}</td>
  <td>{desc}</td>
  <td class=""text-muted small"">{rubro}</td>
  <td class=""text-end text-nowrap"">
    <button class=""btn btn-sm btn-outline-primary me-1"" onclick=""abrirModalEditar({p.Id});event.stopPropagation();"" title=""Editar"">
      <i class=""bi bi-pencil""></i>
    </button>
    <button class=""btn btn-sm btn-outline-danger"" onclick=""confirmarEliminar({p.Id},'{p.Descripcion?.Replace("'", "\\'").Replace("\\", "\\\\") ?? ""}');event.stopPropagation();"" title=""Dar de baja"">
      <i class=""bi bi-trash""></i>
    </button>
  </td>
</tr>");
        }

        if (!lista.Any())
            sb.Append(@"<tr><td colspan=""5"" class=""text-center text-muted py-3"">Sin resultados</td></tr>");

        return Content(sb.ToString(), "text/html");
    }

    // ── AJAX: detalle de un producto (devuelve HTML) ─────────────
    public async Task<IActionResult> OnGetDetalleAsync(int id)
    {
        var p = await _productos.ObtenerPorIdAsync(id);
        if (p is null) return Content("<p class=\"text-danger\">Producto no encontrado.</p>", "text/html");

        string stockBadge = p.StockActual > p.StockMinimo
            ? $"<span class=\"badge bg-success\">En stock ({p.StockActual:N0})</span>"
            : $"<span class=\"badge bg-warning text-dark\">Stock bajo ({p.StockActual:N0})</span>";

        string imgTag = $"<img src=\"/Admin/Productos?handler=Imagen&id={id}&t={DateTime.Now.Ticks}\" class=\"img-fluid rounded mb-2\" style=\"max-height:180px;object-fit:contain;\" onerror=\"this.style.display='none'\" />";

        var html = $@"
<div class=""p-2"">
  <div class=""text-center mb-2"">{imgTag}</div>
  <h6 class=""fw-bold mb-1"">{System.Net.WebUtility.HtmlEncode(p.Descripcion)}</h6>
  <p class=""text-muted small mb-2"">{System.Net.WebUtility.HtmlEncode(p.RubroNombre ?? "Sin rubro")} · {System.Net.WebUtility.HtmlEncode(p.ProveedorNombre ?? "Sin proveedor")}</p>
  <hr class=""my-2""/>
  <div class=""row g-1 small"">
    <div class=""col-6""><span class=""text-muted"">Cód. Prov.:</span> <strong>{System.Net.WebUtility.HtmlEncode(p.CodProveedor ?? "-")}</strong></div>
    <div class=""col-6""><span class=""text-muted"">Cód. Barras:</span> <strong>{System.Net.WebUtility.HtmlEncode(p.CodBarras ?? "-")}</strong></div>
    <div class=""col-6""><span class=""text-muted"">Dolarizado:</span> <strong>{(p.Dolarizado ? "Sí" : "No")}</strong></div>
  </div>
  <hr class=""my-2""/>
  <div class=""row g-1 small"">
    <div class=""col-6""><span class=""text-muted"">Precio lista:</span> <strong class=""text-success"">${p.PrecioLista:N2}</strong></div>
    <div class=""col-6""><span class=""text-muted"">Costo:</span> <strong>${p.PrecioCosto:N2}</strong></div>
  </div>
  <div class=""mt-2"">{stockBadge} <span class=""small text-muted ms-1"">mín. {p.StockMinimo:N0}</span></div>
  {(string.IsNullOrWhiteSpace(p.DescripcionLarga) ? "" : $"<hr class=\"my-2\"/><p class=\"small text-muted mb-0\">{System.Net.WebUtility.HtmlEncode(p.DescripcionLarga)}</p>")}
</div>";

        return Content(html, "text/html");
    }

    // ── AJAX: imagen binaria ─────────────────────────────────────
    public async Task<IActionResult> OnGetImagenAsync(int id)
    {
        var bytes = await _productos.ObtenerImagenAsync(id);
        if (bytes is null or { Length: 0 }) return NotFound();

        string mime = bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? "image/jpeg"
                    : bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 ? "image/png"
                    : bytes.Length >= 4 && bytes[0] == 0x47 && bytes[1] == 0x49 ? "image/gif"
                    : "image/webp";

        return File(bytes, mime);
    }

    // ── AJAX: datos JSON para el modal de edición ────────────────
    public async Task<IActionResult> OnGetProductoJsonAsync(int id)
    {
        var p = await _productos.ObtenerPorIdAsync(id);
        if (p is null) return NotFound();

        return new JsonResult(p, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    // ── POST: subir imagen (devuelve base64 data URL) ────────────
    public async Task<IActionResult> OnPostSubirImagenAsync(IFormFile archivo)
    {
        if (archivo is null)
            return new JsonResult(new { ok = false, error = "Sin archivo." });

        var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        if (!new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" }.Contains(ext))
            return new JsonResult(new { ok = false, error = "Formato no permitido." });

        if (archivo.Length > 5 * 1024 * 1024)
            return new JsonResult(new { ok = false, error = "El archivo supera 5 MB." });

        using var ms = new MemoryStream();
        await archivo.CopyToAsync(ms);
        var base64  = Convert.ToBase64String(ms.ToArray());
        var mimeMap = new Dictionary<string, string>
        {
            [".jpg"]  = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"]  = "image/png",
            [".gif"]  = "image/gif",
            [".webp"] = "image/webp"
        };
        var dataUrl = $"data:{mimeMap[ext]};base64,{base64}";
        return new JsonResult(new { ok = true, dataUrl });
    }

    // ── POST: guardar (alta o modificación) ──────────────────────
    public async Task<IActionResult> OnPostGuardarAsync()
    {
        // Parsear decimales con cultura es-AR o invariant
        static decimal Parse(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
            if (decimal.TryParse(s.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        // Leer imagen desde campo hidden (base64 dataUrl → bytes)
        byte[]? imgBytes = null;
        var dataUrl = Request.Form["imagenDataUrl"].ToString();
        if (!string.IsNullOrWhiteSpace(dataUrl) && dataUrl.Contains(","))
        {
            imgBytes = Convert.FromBase64String(dataUrl.Split(',')[1]);
        }

        // Fix decimals (form sends comma-separated in es-AR)
        Producto.PrecioProveedor = Parse(Request.Form["Producto.PrecioProveedor"]);
        Producto.PrecioCosto     = Parse(Request.Form["Producto.PrecioCosto"]);
        Producto.PrecioLista     = Parse(Request.Form["Producto.PrecioLista"]);
        Producto.StockActual     = Parse(Request.Form["Producto.StockActual"]);
        Producto.StockMinimo     = Parse(Request.Form["Producto.StockMinimo"]);

        // IVA siempre fijo en 1 — no se expone en la interfaz
        Producto.Iva = 1;

        ModelState.Remove(nameof(Producto) + "." + nameof(Producto.PrecioProveedor));
        ModelState.Remove(nameof(Producto) + "." + nameof(Producto.PrecioCosto));
        ModelState.Remove(nameof(Producto) + "." + nameof(Producto.PrecioLista));
        ModelState.Remove(nameof(Producto) + "." + nameof(Producto.Iva));
        ModelState.Remove(nameof(Producto) + "." + nameof(Producto.StockActual));
        ModelState.Remove(nameof(Producto) + "." + nameof(Producto.StockMinimo));

        if (!ModelState.IsValid)
        {
            await CargarDropdownsAsync();
            TempData["Error"] = "Revisá los datos del formulario.";
            return Page();
        }

        if (Producto.Id == 0)
            await _productos.CrearAsync(Producto, imgBytes);
        else
            await _productos.ActualizarAsync(Producto, imgBytes);

        TempData["Mensaje"] = Producto.Id == 0
            ? "Producto creado correctamente."
            : "Producto actualizado correctamente.";

        return RedirectToPage();
    }

    // ── POST: baja lógica ────────────────────────────────────────
    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        await _productos.BajaLogicaAsync(id);
        return new JsonResult(new { ok = true });
    }

    // ── Helper ───────────────────────────────────────────────────
    private async Task CargarDropdownsAsync()
    {
        var rubros = await _rubroRepo.ObtenerRubrosAsync();
        Rubros = rubros
            .Select(r => new SelectListItem(r.Descripcion, r.Id.ToString()))
            .ToList();
        Rubros.Insert(0, new SelectListItem("— Seleccioná un rubro —", ""));

        Proveedores = await _proveedores.ObtenerActivosAsync();
    }
}
