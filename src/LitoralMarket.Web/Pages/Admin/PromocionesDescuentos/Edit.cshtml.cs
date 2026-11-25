using System.Security.Claims;
using System.Text.Json;
using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LitoralMarket.Web.Pages.Admin.PromocionesDescuentos;

/// <summary>Alta / edicion de una promocion por cantidad (NxM) o por porcentaje, con seleccion de productos.</summary>
[Authorize(Roles = "admin")]
public class EditModel : PageModel
{
    private const int MaxResultadosPantalla = 300;     // resultados que se listan al buscar
    private const int MaxAgregarTodos       = 3000;    // tope de "agregar todos los resultados"

    private readonly IPromocionesService _promos;
    private readonly IProveedorRepository _proveedores;
    private readonly IProductoRepository  _productos;
    private readonly IParametrosService   _parametros;

    public EditModel(IPromocionesService promos, IProveedorRepository proveedores, IProductoRepository productos, IParametrosService parametros)
    {
        _promos = promos;
        _proveedores = proveedores;
        _productos = productos;
        _parametros = parametros;
    }

    /// <summary>ecommerce/HabilitarVariantesProducto = 1: solo entonces se puede activar "Acumular las variantes de una misma ficha".</summary>
    public bool VariantesHabilitadas { get; private set; }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public PromoReglaDetalleDto Regla       { get; private set; } = new();
    public bool                 Habilitado  { get; private set; }
    public bool                 TablasListas { get; private set; }
    public List<SelectListItem> Proveedores { get; private set; } = new();
    public List<SelectListItem> Rubros      { get; private set; } = new();
    public int                  MaxPantalla => MaxResultadosPantalla;

    public async Task<IActionResult> OnGetAsync()
    {
        TablasListas = await _promos.TablasListasAsync();
        Habilitado   = await _promos.HabilitadoAsync();
        if (!Habilitado) return Page();
        VariantesHabilitadas = await _parametros.HabilitarVariantesProductoAsync();

        if (Id > 0)
        {
            var r = await _promos.ObtenerAsync(Id);
            if (r == null) return NotFound();
            Regla = r;
        }
        else
        {
            var ahora = HoraArgentina.Ahora();
            Regla = new PromoReglaDetalleDto
            {
                Tipo  = "PORC",
                Desde = new DateTime(ahora.Year, ahora.Month, ahora.Day, ahora.Hour, ahora.Minute, 0),
                Hasta = new DateTime(ahora.Year, ahora.Month, ahora.Day, 23, 59, 0).AddDays(30)
            };
        }

        Proveedores = (await _proveedores.ObtenerActivosAsync())
            .OrderBy(p => p.Nombre).Select(p => new SelectListItem(p.Nombre, p.Id.ToString())).ToList();
        Rubros = (await _productos.ObtenerArbolRubrosAsync())
            .Select(r => new SelectListItem(r.Ruta, r.Id.ToString())).ToList();
        return Page();
    }

    // ── Busqueda de productos: proveedor, rubro, codigo de barras / de proveedor / descripcion ──
    public async Task<IActionResult> OnGetBuscarProductosAsync(
        [FromQuery] int[] proveedores, [FromQuery] int[] rubros, [FromQuery] string? texto, [FromQuery] bool todos = false)
    {
        if (!await _promos.HabilitadoAsync())
            return new JsonResult(new { ok = false, error = "Las promociones no están habilitadas." });
        if (proveedores.Length == 0 && rubros.Length == 0 && string.IsNullOrWhiteSpace(texto))
            return new JsonResult(new { ok = false, error = "Ingrese al menos un criterio de búsqueda." });

        var r = await _promos.BuscarProductosAsync(proveedores, rubros, texto, todos ? MaxAgregarTodos : MaxResultadosPantalla);
        return new JsonResult(new { ok = true, total = r.Total, items = r.Items, limite = todos ? MaxAgregarTodos : MaxResultadosPantalla },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    // ── Guardar (JSON; token en header X-XSRF-TOKEN) ──
    public async Task<IActionResult> OnPostGuardarAsync([FromBody] PromoReglaDetalleDto dto)
    {
        if (!await _promos.HabilitadoAsync())
            return new JsonResult(new { ok = false, error = "Las promociones no están habilitadas." });
        try
        {
            var usuario = User.FindFirstValue(ClaimTypes.Name);
            var id = await _promos.GuardarAsync(dto, usuario);
            return new JsonResult(new { ok = true, id });
        }
        catch (InvalidOperationException ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { ok = false, error = "Error al guardar: " + ex.Message });
        }
    }
}
