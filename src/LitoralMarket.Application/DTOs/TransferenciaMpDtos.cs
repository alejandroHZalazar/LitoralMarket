namespace LitoralMarket.Application.DTOs;

/// <summary>Pago recibido leído de GET /v1/payments/search, ya normalizado.</summary>
public record PagoMpRecibido(
    string          Id,
    decimal         Monto,
    string?         Moneda,
    string?         Status,
    string?         PaymentMethodId,
    string?         PaymentTypeId,
    string?         OperationType,
    string?         PayerId,
    string?         ExternalReference,
    DateTimeOffset? FechaAprobacion);

/// <summary>Resumen de un ciclo de detección/conciliación.</summary>
public record CicloConciliacionResultado(
    bool Habilitado,
    bool Error,
    int  PollingSegundos,
    int  Detectadas,
    int  Asignadas,
    int  ParaRevisar);

public class TransferenciaMpDto
{
    public int       Id               { get; set; }
    public string    MpPaymentId      { get; set; } = string.Empty;
    public decimal   Monto            { get; set; }
    public DateTime? FechaAprobacion  { get; set; }
    public string    Estado           { get; set; } = string.Empty;
    public string?   Motivo           { get; set; }
    public string?   AsignadaPor      { get; set; }
    public int?      FkCobro          { get; set; }
    public int?      PedidoId         { get; set; }

    /// <summary>El administrador todavía puede asignarla a un pedido o ignorarla.</summary>
    public bool Pendiente { get; set; }
}

/// <summary>Cobro de transferencia al que el administrador puede asignar una transferencia recibida.</summary>
public class CobroTransferenciaAsignableDto
{
    public int       CobroId            { get; set; }
    public int       PedidoId           { get; set; }
    public string?   Cliente            { get; set; }
    public decimal   Total              { get; set; }
    public decimal?  MontoTransferencia { get; set; }
    public DateTime? Vencimiento        { get; set; }
    public string    Estado             { get; set; } = string.Empty;
}

public record ResultadoAsignacion(bool Ok, string? Error = null)
{
    public static ResultadoAsignacion Exito() => new(true);
    public static ResultadoAsignacion Fallo(string error) => new(false, error);
}
