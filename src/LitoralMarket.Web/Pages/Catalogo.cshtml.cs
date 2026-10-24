using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Helpers;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

public class CatalogoModel : PageModel
{
    private readonly IProductoRepository _productos;
    private readonly IParametrosService _parametros;

    public CatalogoModel(IProductoRepository productos, IParametrosService parametros)
    {
        _productos = productos;
        _parametros = parametros;
    }

    public int RubroId { get; private set; }
    public string RubroNombre { get; private set; } = string.Empty;
    /// <summary>Cadena de la raíz hasta este rubro (migas de pan). Un solo elemento si es principal.</summary>
    public List<RubroNodoDto> Ruta { get; private set; } = new();
    /// <summary>Subrubros directos del rubro actual, para filtrar más fino.</summary>
    public List<RubroNodoDto> Subrubros { get; private set; } = new();
    public List<ProductoDto> Productos { get; private set; } = new();
    public bool MostrarSinStock { get; private set; }
    public int PaginaActual { get; private set; } = 1;
    public int TotalPaginas { get; private set; } = 1;

    public async Task<IActionResult> OnGetAsync(int rubroId, int pagina = 1)
    {
        RubroId = rubroId;
        MostrarSinStock = await _parametros.MostrarSinStockAsync();
        var porPagina = await _parametros.GetProductosPorPaginaAsync();

        var arbol = await _productos.ObtenerArbolRubrosAsync();
        var rubro = arbol.FirstOrDefault(r => r.Id == rubroId);
        if (rubro is null) return NotFound();

        RubroNombre = rubro.Descripcion;
        Ruta        = RubroArbolHelper.Cadena(arbol, rubroId);
        Subrubros   = arbol.Where(r => r.PadreId == rubroId).ToList();

        // Incluye los productos de todos sus subrubros (ver ProductoRepository).
        var total = await _productos.ContarPorRubroAsync(rubroId, MostrarSinStock);
        TotalPaginas = (int)Math.Ceiling((double)total / porPagina);
        PaginaActual = Math.Clamp(pagina, 1, Math.Max(1, TotalPaginas));

        Productos = await _productos.ObtenerPorRubroAsync(rubroId, MostrarSinStock, PaginaActual, porPagina);
        return Page();
    }
}
