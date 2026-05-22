using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LitoralMarket.Infrastructure.Services;

public class ParametrosService : IParametrosService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public ParametrosService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<string?> GetValorAsync(string modulo, string parametro)
    {
        var cacheKey = $"param_{modulo}_{parametro}";
        if (_cache.TryGetValue(cacheKey, out string? valor))
            return valor;

        var param = await _db.Parametros
            .FirstOrDefaultAsync(p => p.Modulo == modulo && p.ParametroNombre == parametro);

        valor = param?.Valor;
        _cache.Set(cacheKey, valor, CacheDuration);
        return valor;
    }

    public async Task<string> GetModoAccesoAsync() =>
        await GetValorAsync("ecommerce", "modoAcceso") ?? "publico";

    public async Task<bool> MostrarSinStockAsync() =>
        (await GetValorAsync("ecommerce", "mostrarSinStock")) == "1";

    public async Task<int> GetProductosPorPaginaAsync()
    {
        var val = await GetValorAsync("ecommerce", "productosPorPagina");
        return int.TryParse(val, out var n) ? n : 12;
    }

    public async Task<string> GetTituloEcommerceAsync() =>
        await GetValorAsync("ecommerce", "titulo") ?? "LitoralMarket";

    public async Task<string?> GetNombreEmpresaAsync() =>
        await GetValorAsync("empresa", "nombre");

    public async Task<byte[]?> GetLogoAsync()
    {
        const string cacheKey = "param_logo_bytes";
        if (_cache.TryGetValue(cacheKey, out byte[]? logoBytes))
            return logoBytes;

        var param = await _db.Parametros
            .FirstOrDefaultAsync(p => p.Modulo == "login" && p.ParametroNombre == "imagen");

        logoBytes = param?.Imagen;
        if (logoBytes is { Length: > 0 })
            _cache.Set(cacheKey, logoBytes, CacheDuration);

        return logoBytes;
    }
}
