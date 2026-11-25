using System.Globalization;

namespace LitoralMarket.Application.Helpers;

/// <summary>
/// Reglas de venta por múltiplos (Productos.cantidadMinimaVenta), compartidas entre
/// carrito y checkout para no duplicar la validación en cada lugar que la necesita.
/// </summary>
public static class CantidadVentaHelper
{
    /// <summary>
    /// true si <paramref name="cantidadMinimaVenta"/> impone una venta en múltiplos
    /// (es decir, es mayor a 1). Si es NULL o &lt;= 1, el producto no tiene restricción.
    /// </summary>
    public static bool RequiereMultiplo(decimal? cantidadMinimaVenta) =>
        cantidadMinimaVenta is decimal m && m > 1;

    /// <summary>
    /// true si <paramref name="cantidad"/> es una cantidad de venta válida para el producto:
    /// positiva, y si <paramref name="cantidadMinimaVenta"/> exige múltiplo, múltiplo exacto de ella.
    /// </summary>
    public static bool EsCantidadValida(decimal cantidad, decimal? cantidadMinimaVenta)
    {
        if (cantidad <= 0) return false;
        if (!RequiereMultiplo(cantidadMinimaVenta)) return true;
        return cantidad % cantidadMinimaVenta!.Value == 0m;
    }

    // ── Límite por stock (ecommerce/LimitarCantidadPorStock = 1) ──────────────
    // Una sola regla para catálogo, detalle, carrito y checkout. Son funciones puras: no leen ni
    // modifican stock.

    /// <summary>Menor cantidad comprable: el múltiplo mínimo, o 0,01 (fraccionado), o 1.</summary>
    public static decimal UnidadMinima(decimal? cantidadMinimaVenta, bool fraccionado) =>
        RequiereMultiplo(cantidadMinimaVenta) ? cantidadMinimaVenta!.Value : (fraccionado ? 0.01m : 1m);

    /// <summary>
    /// Mayor cantidad que se puede pedir sin superar el stock, respetando la forma de venta del producto:
    /// con múltiplo, el mayor múltiplo que entra; fraccionado, el stock a 2 decimales; resto, la parte entera.
    /// Stock &lt;= 0 → 0.
    /// </summary>
    public static decimal MaximoPermitido(decimal stock, decimal? cantidadMinimaVenta, bool fraccionado)
    {
        if (stock <= 0m) return 0m;
        if (RequiereMultiplo(cantidadMinimaVenta))
        {
            var m = cantidadMinimaVenta!.Value;
            return Math.Floor(stock / m) * m;
        }
        return fraccionado ? Math.Floor(stock * 100m) / 100m : Math.Floor(stock);
    }

    /// <summary>Cantidad para mostrar en mensajes: hasta 2 decimales, sin ceros de relleno.</summary>
    public static string Formatear(decimal cantidad) =>
        cantidad.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>
    /// Stock para mostrar en mensajes: se TRUNCA a 2 decimales (no se redondea) para no aparentar más
    /// existencias de las reales (2,505 se muestra 2,5, no 2,51).
    /// </summary>
    public static string FormatearStock(decimal stock) =>
        Formatear(Math.Truncate(stock * 100m) / 100m);

    /// <summary>
    /// Mensaje para el cliente cuando lo pedido supera el stock. <paramref name="yaEnCarrito"/> es lo que ya
    /// tiene el carrito del mismo producto (0 si se está reemplazando la cantidad de una línea).
    /// </summary>
    public static string MensajeSuperaStock(string? descripcion, decimal stock, decimal maximo,
        decimal yaEnCarrito, decimal unidadMinima)
    {
        var nombre = string.IsNullOrWhiteSpace(descripcion) ? "este producto" : $"'{descripcion}'";

        if (stock <= 0m)
            return $"Sin stock disponible de {nombre}.";

        if (maximo < unidadMinima)
            return $"Hay {FormatearStock(stock)} disponibles de {nombre}, menos que la cantidad mínima de venta ({Formatear(unidadMinima)}).";

        if (yaEnCarrito > 0m)
        {
            var restante = maximo - yaEnCarrito;
            return restante <= 0m
                ? $"Ya tenés en el carrito la cantidad máxima disponible de {nombre} ({Formatear(maximo)})."
                : $"Ya tenés {Formatear(yaEnCarrito)} en el carrito y hay {FormatearStock(stock)} disponibles de {nombre}. Podés agregar hasta {Formatear(restante)} más.";
        }

        return $"Solo hay {FormatearStock(stock)} disponibles de {nombre}. La cantidad máxima que podés pedir es {Formatear(maximo)}.";
    }
}
