namespace LitoralMarket.Application.Interfaces;

public interface IParametrosService
{
    Task<string?> GetValorAsync(string modulo, string parametro);
    Task<string> GetModoAccesoAsync();
    Task<bool> MostrarSinStockAsync();
    /// <summary>ecommerce/preciosSinIVA == "1" → los precios mostrados no incluyen IVA.</summary>
    Task<bool> MostrarPreciosSinIvaAsync();
    /// <summary>ecommerce/CodProveedorEnCatalogo == "1" → se muestra Productos.codProveedor en catálogo y detalle.</summary>
    Task<bool> MostrarCodProveedorEnCatalogoAsync();
    /// <summary>ecommerce/CarruselPromociones == "1" → se muestra el carrusel de promociones en Inicio.</summary>
    Task<bool> MostrarCarruselPromocionesAsync();
    /// <summary>
    /// ecommerce/ProductosInicioConfigurables == "1" → el Inicio muestra los productos que elige el administrador.
    /// Cualquier otro valor (no existe, NULL, 0...) → muestra automáticamente los últimos productos.
    /// </summary>
    Task<bool> SeleccionManualInicioHabilitadaAsync();
    /// <summary>
    /// ecommerce/IntervaloCarruselPromociones, en SEGUNDOS, entre 2 y 60. Si no existe, es NULL o no es
    /// un entero válido en ese rango, devuelve el predeterminado (5).
    /// </summary>
    Task<int> GetIntervaloCarruselPromocionesAsync();
    Task<int> GetProductosPorPaginaAsync();
    Task<string> GetTituloEcommerceAsync();
    Task<string?> GetNombreEmpresaAsync();
    Task<byte[]?> GetLogoAsync();

    // Colores predominantes (marca) — configurables por el admin, globales
    Task<string?> GetColorPrimarioAsync();
    Task<string?> GetColorAcentoAsync();

    // Upsert genérico (crea o actualiza) — invalida cache de la clave
    Task SetValorAsync(string modulo, string parametro, string? valor);
}
