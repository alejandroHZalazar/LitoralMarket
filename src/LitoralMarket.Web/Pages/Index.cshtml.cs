using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IProductoRepository _productos;
    private readonly IParametrosService _parametros;

    public IndexModel(IProductoRepository productos, IParametrosService parametros)
    {
        _productos = productos;
        _parametros = parametros;
    }

    public List<ProductoDto> Productos { get; private set; } = new();
    public bool MostrarSinStock { get; private set; }
    public string Titulo { get; private set; } = string.Empty;
    public int PaginaActual { get; private set; } = 1;
    public int TotalPaginas { get; private set; } = 1;

    public async Task OnGetAsync(int pagina = 1)
    {
        MostrarSinStock = await _parametros.MostrarSinStockAsync();
        Titulo = await _parametros.GetTituloEcommerceAsync();
        var porPagina = await _parametros.GetProductosPorPaginaAsync();

        var rubros = await _productos.ObtenerRubrosAsync();
        if (!rubros.Any()) return;

        var primerRubro = rubros.First();
        var total = await _productos.ContarPorRubroAsync(primerRubro.Id, MostrarSinStock);
        TotalPaginas = (int)Math.Ceiling((double)total / porPagina);
        PaginaActual = Math.Clamp(pagina, 1, Math.Max(1, TotalPaginas));

        Productos = await _productos.ObtenerPorRubroAsync(primerRubro.Id, MostrarSinStock, PaginaActual, porPagina);
    }
}
