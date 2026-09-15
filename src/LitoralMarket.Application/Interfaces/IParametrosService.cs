namespace LitoralMarket.Application.Interfaces;

public interface IParametrosService
{
    Task<string?> GetValorAsync(string modulo, string parametro);
    Task<string> GetModoAccesoAsync();
    Task<bool> MostrarSinStockAsync();
    /// <summary>ecommerce/preciosSinIVA == "1" → los precios mostrados no incluyen IVA.</summary>
    Task<bool> MostrarPreciosSinIvaAsync();
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
