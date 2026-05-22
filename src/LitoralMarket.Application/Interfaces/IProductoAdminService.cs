using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IProductoAdminService
{
    /// <summary>Busca productos activos. tipo: 0=CodProveedor, 1=CodBarras, 2=Descripcion.</summary>
    Task<List<ProductoAdminDto>> BuscarAsync(int tipo, string valor);

    Task<ProductoAdminDto?> ObtenerPorIdAsync(int id);
    Task<byte[]?>           ObtenerImagenAsync(int id);

    /// <summary>Crea producto + stock + precio + costo. Devuelve el Id generado.</summary>
    Task<int> CrearAsync(ProductoAdminDto dto, byte[]? imagen);

    Task ActualizarAsync(ProductoAdminDto dto, byte[]? imagen);

    /// <summary>Baja lógica (Baja = true).</summary>
    Task BajaLogicaAsync(int id);
}
