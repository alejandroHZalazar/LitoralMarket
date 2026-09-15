namespace LitoralMarket.Web.Pages.Shared;

/// <summary>
/// Datos para renderizar el control de cantidad (+/-) compartido por catálogo,
/// detalle de producto y carrito — una sola lógica para las 3 pantallas.
/// </summary>
/// <param name="Valor">Cantidad actual a mostrar. 0 = todavía no hay cantidad elegida (alta desde catálogo/detalle).</param>
/// <param name="Fraccionado">Si el producto admite cantidades no enteras (paso 0.01) cuando no hay cantidadMinimaVenta.</param>
/// <param name="CantidadMinimaVenta">Productos.cantidadMinimaVenta. Si es &gt; 1, el input queda readonly y solo se mueve en múltiplos de ese valor.</param>
/// <param name="AutoGuardar">true = al cambiar el valor (típicamente en el carrito), se envía el formulario contenedor automáticamente.</param>
public record CantidadStepperVm(
    decimal Valor,
    bool Fraccionado,
    decimal? CantidadMinimaVenta,
    bool AutoGuardar = false);
