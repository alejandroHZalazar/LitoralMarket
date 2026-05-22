using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

public class BuscarModel : PageModel
{
    private readonly IProductoRepository _productos;
    private readonly IParametrosService _parametros;

    public BuscarModel(IProductoRepository productos, IParametrosService parametros)
    {
        _productos = productos;
        _parametros = parametros;
    }

    public string Termino { get; private set; } = string.Empty;
    public List<ProductoDto> Productos { get; private set; } = new();
    public bool MostrarSinStock { get; private set; }
    public int PaginaActual { get; private set; } = 1;
    public int TotalPaginas { get; private set; } = 1;
    public int TotalResultados { get; private set; }

    public async Task<IActionResult> OnGetAsync(string q, int pagina = 1)
    {
        if (string.IsNullOrWhiteSpace(q))
            return RedirectToPage("/Index");

        Termino = q.Trim();
        MostrarSinStock = await _parametros.MostrarSinStockAsync();
        var porPagina = await _parametros.GetProductosPorPaginaAsync();

        TotalResultados = await _productos.ContarBusquedaAsync(Termino, MostrarSinStock);
        TotalPaginas = (int)Math.Ceiling((double)TotalResultados / porPagina);
        PaginaActual = Math.Clamp(pagina, 1, Math.Max(1, TotalPaginas));

        Productos = await _productos.BuscarAsync(Termino, MostrarSinStock, PaginaActual, porPagina);
        return Page();
    }
}
