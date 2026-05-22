using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

public interface IEstadisticasService
{
    /// <param name="desde">Inicio del período (inclusive).</param>
    /// <param name="hasta">Fin del período (inclusive, hasta 23:59:59).</param>
    Task<EstadisticasDto> ObtenerAsync(DateTime desde, DateTime hasta);
}
