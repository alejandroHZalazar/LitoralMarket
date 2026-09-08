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
}
