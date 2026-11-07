using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IPromocionCarruselService
{
    /// <summary>Promociones en el orden definido por el administrador (sin blobs, cacheado).</summary>
    Task<List<PromocionCarruselDto>> ListarAsync();

    /// <summary>Imagen de una promoción para servir por URL.</summary>
    Task<PromocionImagen?> ObtenerImagenAsync(int id);

    /// <summary>Agrega una promoción al final. Valida el tipo real y el tamaño de la imagen.</summary>
    Task<ResultadoPromocion> CrearAsync(byte[] imagen, string? titulo);

    Task<ResultadoPromocion> ReemplazarImagenAsync(int id, byte[] imagen);

    Task<ResultadoPromocion> ActualizarTituloAsync(int id, string? titulo);

    /// <summary>Elimina la promoción y renumera el orden de las restantes.</summary>
    Task<ResultadoPromocion> EliminarAsync(int id);

    /// <summary>Define el orden: la posición en la lista pasa a ser el orden. Debe incluir todas las promociones.</summary>
    Task<ResultadoPromocion> ReordenarAsync(IReadOnlyList<int> idsEnOrden);
}
