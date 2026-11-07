using System.Text.Json;
using LitoralMarket.Application.Helpers;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages.Admin.Productos;

/// <summary>
/// Selección manual de los productos de la página de inicio (hasta 20). Solo disponible con el
/// parámetro ecommerce/ProductosInicioConfigurables = 1; con otro valor el Inicio muestra los últimos 20.
/// </summary>
[Authorize(Roles = "admin")]
public class InicioModel : PageModel
{
    private readonly IProductosInicioService _inicio;
    private readonly IProductoAdminService _productos;
    private readonly IParametrosService _parametros;

    public InicioModel(IProductosInicioService inicio, IProductoAdminService productos, IParametrosService parametros)
    {
        _inicio = inicio;
        _productos = productos;
        _parametros = parametros;
    }

    public bool Habilitado { get; private set; }
    public int Maximo => ProductosInicio.Maximo;
    /// <summary>Selección guardada serializada para la página (el serializador escapa &lt; &gt; &amp;).</summary>
    public string SeleccionJson { get; private set; } = "[]";

    public async Task OnGetAsync()
    {
        Habilitado = await _parametros.SeleccionManualInicioHabilitadaAsync();
        if (!Habilitado) return;

        var seleccion = await _inicio.ObtenerSeleccionAsync();
        SeleccionJson = JsonSerializer.Serialize(seleccion.Select(s => new
        {
            id           = s.ProductoId,
            codProveedor = s.CodProveedor,
            codBarras    = s.CodBarras,
            descripcion  = s.Descripcion,
            stock        = s.Stock,
            visible      = s.Visible,
            motivo       = s.Motivo
        }));
    }

    // ── AJAX: buscar productos para agregar (mismo buscador que Ingreso de stock) ──
    public async Task<IActionResult> OnGetBuscarAsync(int tipo, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return Resultado(new { ok = false, error = "Escribí al menos un carácter para buscar." });

        var lista = await _productos.BuscarAsync(tipo, valor.Trim());
        return Resultado(new
        {
            ok = true,
            items = lista.Select(p => new
            {
                id           = p.Id,
                codProveedor = p.CodProveedor,
                codBarras    = p.CodBarras,
                descripcion  = p.Descripcion,
                rubro        = p.RubroRuta ?? p.RubroNombre
            })
        });
    }

    // ── POST (JSON + token en header X-XSRF-TOKEN) ──────────────────────────
    public async Task<IActionResult> OnPostGuardarAsync([FromBody] GuardarRequest req)
    {
        // Una lista vacía es válida (vacía la selección), pero un cuerpo mal formado no debe vaciarla.
        if (req?.Ids is null)
            return Resultado(new { ok = false, error = "Solicitud inválida." });

        var r = await _inicio.GuardarSeleccionAsync(req.Ids);
        return Resultado(new { ok = r.Ok, error = r.Error });
    }

    private static JsonResult Resultado(object datos) => new(datos);

    public record GuardarRequest(List<int> Ids);
}
