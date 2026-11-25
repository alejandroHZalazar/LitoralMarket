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

    /// <summary>Solo para fichas con variantes (ecommerce/HabilitarVariantesProducto = 1): las variantes a elegir.</summary>
    public FichaCatalogoDto? Ficha { get; private set; }
    /// <summary>Variante a preseleccionar (viene de un enlace directo al id de una variante).</summary>
    public int? VarianteInicialId { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, int? v = null)
    {
        MostrarSinStock = await _parametros.MostrarSinStockAsync();
        Producto = await _productos.ObtenerPorIdAsync(id);

        if (Producto is null)
        {
            // Un enlace al id de una variante (carrito viejo, correo, favoritos) lleva a su ficha con la variante elegida.
            // Con el parámetro apagado esta consulta ni se hace.
            var principal = await _productos.ObtenerPrincipalDeVarianteAsync(id);
            if (principal.HasValue) return RedirectToPage("/Producto", new { id = principal.Value, v = id });
            return NotFound();
        }
        if (!Producto.TieneStock && !MostrarSinStock) return NotFound();

        if (Producto.TieneVariantes)
        {
            Ficha = await _productos.ObtenerVariantesAsync(id);
            if (Ficha is null || Ficha.Variantes.Count == 0) return NotFound();
            if (v.HasValue && Ficha.Variantes.Any(x => x.Id == v.Value)) VarianteInicialId = v;

            // Tope por stock de cada variante (solo con ecommerce/LimitarCantidadPorStock = 1). La cantidad mínima y
            // el fraccionado se copian de la ficha a cada variante, así que valen los de la ficha.
            if (await _parametros.LimitarCantidadPorStockAsync())
            {
                foreach (var variante in Ficha.Variantes.Where(x => x.Stock > 0))
                    variante.MaximoPermitido = CantidadVentaHelper.MaximoPermitido(
                        variante.Stock, Producto.CantidadMinimaVenta, Producto.Fraccionado);
            }
        }

        // Metadatos de imágenes (sin blobs) para la galería
        Imagenes = await _productos.ObtenerImagenesAsync(id);

        if (Producto.RubroId.HasValue)
            RutaRubro = RubroArbolHelper.Cadena(await _productos.ObtenerArbolRubrosAsync(), Producto.RubroId.Value);

        return Page();
    }
}
