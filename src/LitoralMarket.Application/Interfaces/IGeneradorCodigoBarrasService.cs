namespace LitoralMarket.Application.Interfaces;

/// <summary>
/// Códigos de barras / de proveedor automáticos al dar de alta productos. Misma lógica y MISMO contador que Comercial Web
/// (ambas aplicaciones comparten la base): parámetros productos/generaCodigoBarras ('1' = activo) y
/// productos/proximoCodigoBarras (contador). Apagado o inexistente = comportamiento de siempre.
/// </summary>
public interface IGeneradorCodigoBarrasService
{
    /// <summary>productos/generaCodigoBarras = '1' (sin caché: se consulta antes de abrir la transacción del alta).</summary>
    Task<bool> HabilitadoAsync();

    /// <summary>
    /// Reserva <paramref name="cantidad"/> códigos libres (001, 002 … 999, 1000 …): ninguno existe como código de barras ni de
    /// proveedor de ningún producto (incluidos los dados de baja) ni está en <paramref name="excluir"/>.
    /// Debe llamarse dentro de la transacción del alta y como PRIMERA lectura: bloquea la fila del contador hasta que la
    /// transacción confirme o revierta.
    /// </summary>
    /// <exception cref="InvalidOperationException">No hay transacción activa o falta el parámetro del contador.</exception>
    Task<List<string>> ReservarAsync(int cantidad, IEnumerable<string>? excluir = null);
}
