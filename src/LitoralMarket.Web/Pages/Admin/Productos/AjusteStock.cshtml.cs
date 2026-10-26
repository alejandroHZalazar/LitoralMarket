using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LitoralMarket.Web.Pages.Admin.Productos;

[Authorize(Roles = "admin")]
public class AjusteStockModel : PageModel
{
    private readonly IAjusteStockService  _ajuste;
    private readonly IProveedorRepository _proveedores;
    private readonly IProductoRepository  _productos;

    public AjusteStockModel(IAjusteStockService ajuste, IProveedorRepository proveedores, IProductoRepository productos)
    {
        _ajuste      = ajuste;
        _proveedores = proveedores;
        _productos   = productos;
    }

    public List<SelectListItem> Rubros      { get; private set; } = new();
    public List<ProveedorDto>   Proveedores { get; private set; } = new();
    /// <summary>Decimales permitidos en el stock (parámetro productos/decimalesCant).</summary>
    public int Decimales { get; private set; }

    public async Task OnGetAsync()
    {
        Rubros = (await _productos.ObtenerArbolRubrosAsync())
            .Select(r => new SelectListItem(r.Ruta, r.Id.ToString()))
            .ToList();
        Proveedores = await _proveedores.ObtenerActivosAsync();
        Decimales   = await _ajuste.DecimalesStockAsync();
    }

    // ── AJAX: buscar. modo "cat" = proveedor/rubro, modo "txt" = texto ──
    public async Task<IActionResult> OnGetBuscarAsync(
        string modo, int? proveedorId, int? rubroId, int tipo, string? texto, bool ocultarBajas = true)
    {
        var filtro = new AjusteStockFiltro { OcultarBajas = ocultarBajas };

        if (modo == "txt")
        {
            if (string.IsNullOrWhiteSpace(texto))
                return Resultado(new { ok = false, error = "Escribí un texto para buscar." });
            filtro.Tipo  = tipo;
            filtro.Texto = texto;
        }
        else
        {
            if (proveedorId is null && rubroId is null)
                return Resultado(new { ok = false, error = "Elegí un proveedor y/o un rubro." });
            filtro.ProveedorId = proveedorId;
            filtro.RubroId     = rubroId;
        }

        var r = await _ajuste.BuscarAsync(filtro);
        return Resultado(new
        {
            ok       = true,
            truncado = r.Truncado,
            limite   = r.Limite,
            items    = r.Items.Select(i => new
            {
                id           = i.Id,
                codProveedor = i.CodProveedor,
                codBarras    = i.CodBarras,
                descripcion  = i.Descripcion,
                stock        = i.Stock,
                baja         = i.Baja
            })
        });
    }

    // ── POST (JSON + token en header X-XSRF-TOKEN) ──────────────────
    public async Task<IActionResult> OnPostAjustarAsync([FromBody] AjustarRequest req)
    {
        if (req?.Lineas is not { Count: > 0 })
            return Resultado(new { ok = false, error = "No hay ajustes para guardar." });

        var r = await _ajuste.AjustarAsync(
            req.Lineas.Select(l => new AjusteStockLinea(l.Id, l.StockLeido, l.StockNuevo)).ToList());

        return Resultado(new
        {
            ok         = r.Ok,
            ajustados  = r.Ajustados,
            error      = r.Error,
            conflictos = r.Conflictos.Select(c => new { id = c.ProductoId, stockActual = c.StockActual })
        });
    }

    public async Task<IActionResult> OnPostBajaAsync([FromBody] BajaRequest req)
    {
        if (req is null || req.Id <= 0)
            return Resultado(new { ok = false, error = "Producto inválido." });

        var ok = await _ajuste.CambiarBajaAsync(req.Id, req.Baja);
        return Resultado(ok ? new { ok = true, error = (string?)null } : new { ok = false, error = (string?)"Producto no encontrado." });
    }

    private static JsonResult Resultado(object datos) => new(datos);

    public record LineaRequest(int Id, decimal StockLeido, decimal StockNuevo);
    public record AjustarRequest(List<LineaRequest> Lineas);
    public record BajaRequest(int Id, bool Baja);
}
