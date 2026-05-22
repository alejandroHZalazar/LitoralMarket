using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

public class ProductoDetalleModel : PageModel
{
    private readonly IProductoRepository _productos;
    private readonly IParametrosService _parametros;

    public ProductoDetalleModel(IProductoRepository productos, IParametrosService parametros)
    {
        _productos = productos;
        _parametros = parametros;
    }

    public ProductoDto? Producto { get; private set; }
    public bool MostrarSinStock { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        MostrarSinStock = await _parametros.MostrarSinStockAsync();
        Producto = await _productos.ObtenerPorIdAsync(id);

        if (Producto is null) return NotFound();
        if (!Producto.TieneStock && !MostrarSinStock) return NotFound();

        return Page();
    }
}
