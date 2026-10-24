using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Helpers;
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
    public List<ProductoImagenDto> Imagenes { get; private set; } = new();
    /// <summary>Cadena rubro principal → subrubro del producto (cada tramo enlaza a su catálogo).</summary>
    public List<RubroNodoDto> RutaRubro { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        MostrarSinStock = await _parametros.MostrarSinStockAsync();
        Producto = await _productos.ObtenerPorIdAsync(id);

        if (Producto is null) return NotFound();
        if (!Producto.TieneStock && !MostrarSinStock) return NotFound();

        // Metadatos de imágenes (sin blobs) para la galería
        Imagenes = await _productos.ObtenerImagenesAsync(id);

        if (Producto.RubroId.HasValue)
            RutaRubro = RubroArbolHelper.Cadena(await _productos.ObtenerArbolRubrosAsync(), Producto.RubroId.Value);

        return Page();
    }
}
