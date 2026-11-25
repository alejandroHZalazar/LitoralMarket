namespace LitoralMarket.Application.DTOs;

/// <summary>Resultado de sincronizar el carrito con el catálogo (precios, costos y stock).</summary>
/// <param name="Eliminados">Productos quitados del carrito por falta de stock.</param>
/// <param name="Ajustados">Líneas cuya cantidad se redujo al stock disponible (solo con ecommerce/LimitarCantidadPorStock = 1).</param>
public record SincronizacionCarrito(List<string> Eliminados, List<string> Ajustados);
