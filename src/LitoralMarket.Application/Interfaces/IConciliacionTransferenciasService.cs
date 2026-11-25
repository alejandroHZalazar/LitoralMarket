using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Interfaces;

/// <summary>
/// Detecta las transferencias entrantes de la cuenta de Mercado Pago y las asocia a los cobros
/// de transferencia pendientes (importe exacto + ventana de tiempo). Lo que no coincide queda
/// registrado para que el administrador lo resuelva.
/// </summary>
public interface IConciliacionTransferenciasService
{
    /// <summary>
    /// true si las tablas nuevas (cobros_transferencia y transferencias_mp) existen en esta base,
    /// es decir, si ya se corrió sql/create_transferencias_mp.sql.
    /// </summary>
    Task<bool> TablasListasAsync();

    /// <summary>
    /// Un ciclo completo: cancela cobros vencidos, consulta Mercado Pago, registra y concilia.
    /// No hace nada si ecommerce/metodoPagoTransferenciaMP no vale '1'.
    /// </summary>
    Task<CicloConciliacionResultado> EjecutarCicloAsync(CancellationToken ct = default);

    /// <summary>
    /// Registra (una sola vez por id) y concilia los pagos recibidos. Descarta lo que no es una
    /// transferencia entrante aprobada. Devuelve el resumen de lo procesado.
    /// </summary>
    Task<CicloConciliacionResultado> ProcesarPagosAsync(IReadOnlyList<PagoMpRecibido> pagos, CancellationToken ct = default);

    Task<List<TransferenciaMpDto>> ListarAsync(string? estado, int maximo = 200);

    Task<List<CobroTransferenciaAsignableDto>> ListarCobrosAsignablesAsync();

    /// <summary>Asigna una transferencia sin resolver a un cobro de transferencia y confirma el pedido.</summary>
    Task<ResultadoAsignacion> AsignarManualAsync(int transferenciaId, int cobroId);

    /// <summary>Marca una transferencia sin resolver como ignorada (no corresponde a ningún pedido).</summary>
    Task<ResultadoAsignacion> IgnorarAsync(int transferenciaId);
}
