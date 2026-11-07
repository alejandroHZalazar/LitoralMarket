using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Helpers;
using LitoralMarket.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LitoralMarket.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IProductoRepository _productos;
    private readonly IParametrosService _parametros;
    private readonly IPromocionCarruselService _promociones;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(IProductoRepository productos, IParametrosService parametros, IPromocionCarruselService promociones,
                      ILogger<IndexModel> logger)
    {
        _logger = logger;
        _productos = productos;
        _parametros = parametros;
        _promociones = promociones;
    }

    /// <summary>
    /// Productos del Inicio (máximo <see cref="ProductosInicio.Maximo"/>): los últimos por id descendente,
    /// o los que eligió el administrador si ecommerce/ProductosInicioConfigurables = 1.
    /// </summary>
    public List<ProductoDto> Productos { get; private set; } = new();
    /// <summary>True si la lista viene de la selección manual del administrador.</summary>
    public bool SeleccionManual { get; private set; }
    public bool MostrarSinStock { get; private set; }
    /// <summary>Promociones del carrusel en el orden del administrador. Vacía si el parámetro no es "1".</summary>
    public List<PromocionCarruselDto> Promociones { get; private set; } = new();
    /// <summary>Segundos entre avances automáticos del carrusel (parámetro IntervaloCarruselPromociones).</summary>
    public int IntervaloCarruselSegundos { get; private set; } = 5;
    public string Titulo { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        MostrarSinStock = await _parametros.MostrarSinStockAsync();
        Titulo = await _parametros.GetTituloEcommerceAsync();

        // Carrusel: solo si el parámetro vale exactamente "1". La lista viene cacheada y sin blobs;
        // cada imagen se pide por URL aparte.
        if (await _parametros.MostrarCarruselPromocionesAsync())
        {
            try
            {
                Promociones = await _promociones.ListarAsync();
                IntervaloCarruselSegundos = await _parametros.GetIntervaloCarruselPromocionesAsync();
            }
            catch (Exception ex)
            {
                // Ej.: parámetro activado antes de crear la tabla. La portada no debe caerse por el carrusel.
                _logger.LogError(ex, "No se pudo cargar el carrusel de promociones");
            }
        }

        // Productos: una sola lista, máximo 20, con los filtros de visibilidad del catálogo (baja y stock).
        if (await _parametros.SeleccionManualInicioHabilitadaAsync())
        {
            try
            {
                Productos = await _productos.ObtenerSeleccionInicioAsync(MostrarSinStock, ProductosInicio.Maximo);
                SeleccionManual = true;
                return;
            }
            catch (Exception ex)
            {
                // Ej.: parámetro activado antes de crear la tabla: se cae a los últimos productos.
                _logger.LogError(ex, "No se pudo leer la selección manual del Inicio; se muestran los últimos productos");
            }
        }

        Productos = await _productos.ObtenerUltimosAsync(ProductosInicio.Maximo, MostrarSinStock);
    }
}
