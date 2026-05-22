namespace LitoralMarket.Application.Interfaces;

public interface IParametrosService
{
    Task<string?> GetValorAsync(string modulo, string parametro);
    Task<string> GetModoAccesoAsync();
    Task<bool> MostrarSinStockAsync();
    Task<int> GetProductosPorPaginaAsync();
    Task<string> GetTituloEcommerceAsync();
    Task<string?> GetNombreEmpresaAsync();
    Task<byte[]?> GetLogoAsync();
}
