using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

/// <summary>
/// Productos de una promoción por cantidad / porcentaje (destino de los banners del carrusel). Solo lista los productos
/// asociados a la regla y únicamente si está vigente: eliminada, inactiva, vencida o aún no vigente muestra un aviso y
/// ningún producto. El descuento no se calcula acá: se aplica como siempre al agregar al carrito.
/// </summary>
public class PromocionModel : PageModel
{
    private readonly IPromocionesService _promos;
    private readonly IProductoRepository _productos;
    private readonly IParametrosService _parametros;

    public PromocionModel(IPromocionesService promos, IProductoRepository productos, IParametrosService parametros)
    {
        _promos = promos;
        _productos = productos;
        _parametros = parametros;
    }

    public PromoDestinoDto Promo { get; private set; } = new();
    public List<ProductoDto> Productos { get; private set; } = new();
    public bool MostrarSinStock { get; private set; }
    public int PaginaActual { get; private set; } = 1;
    public int TotalPaginas { get; private set; } = 1;
    public int TotalResultados { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, int pagina = 1)
    {
        Promo = await _promos.ObtenerParaCatalogoAsync(id);
        MostrarSinStock = await _parametros.MostrarSinStockAsync();

        // Promoción no disponible: la página responde 200 con el aviso (el enlace del banner no debe terminar en un 404 críptico)
        // y nunca se listan productos.
        if (Promo.Estado != EstadoPromoDestino.Vigente) return Page();

        var porPagina = await _parametros.GetProductosPorPaginaAsync();
        var (items, total) = await _productos.BuscarPorIdsAsync(Promo.ProductoIds, MostrarSinStock, pagina, porPagina);
        TotalResultados = total;
        TotalPaginas = Math.Max(1, (int)Math.Ceiling((double)total / porPagina));
        PaginaActual = Math.Clamp(pagina, 1, TotalPaginas);
        if (PaginaActual != pagina)       // pagina fuera de rango: se vuelve a pedir la ultima valida
            (items, _) = await _productos.BuscarPorIdsAsync(Promo.ProductoIds, MostrarSinStock, PaginaActual, porPagina);
        Productos = items;
        return Page();
    }
}
