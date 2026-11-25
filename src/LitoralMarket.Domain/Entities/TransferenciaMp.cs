namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Transferencia entrante detectada en la cuenta de Mercado Pago (GET /v1/payments/search).
/// Se guarda una sola vez por <see cref="MpPaymentId"/> (índice UNIQUE): es la garantía de que
/// una misma transferencia no se procese dos veces.
/// </summary>
public class TransferenciaMp
{
    public int Id { get; set; }

    /// <summary>Id del pago en Mercado Pago. Único.</summary>
    public string MpPaymentId { get; set; } = string.Empty;

    public decimal Monto { get; set; }
    public string? Moneda { get; set; }

    /// <summary>Fecha de acreditación según Mercado Pago, en hora del servidor.</summary>
    public DateTime? FechaAprobacion { get; set; }

    public DateTime FechaDetectada { get; set; } = DateTime.Now;

    public string? PaymentMethodId { get; set; }
    public string? OperationType { get; set; }

    /// <summary>Id del pagador cuando Mercado Pago lo informa (no incluye nombre ni documento).</summary>
    public string? PayerId { get; set; }

    public string? StatusMp { get; set; }

    /// <summary>Ver <see cref="TransferenciaMpEstado"/>.</summary>
    public string Estado { get; set; } = TransferenciaMpEstado.SinAsignar;

    /// <summary>Cobro (cobros_ecommerce.id) al que quedó asignada.</summary>
    public int? FkCobro { get; set; }

    public string? Motivo { get; set; }

    /// <summary>auto | admin</summary>
    public string? AsignadaPor { get; set; }

    /// <summary>Resumen de auditoría con los campos usados (sin datos personales del pagador).</summary>
    public string? JsonMp { get; set; }
}

public static class TransferenciaMpEstado
{
    public const string SinAsignar      = "sin_asignar";
    public const string Asignada        = "asignada";
    public const string Ambigua         = "ambigua";
    public const string ImporteDistinto = "importe_distinto";
    public const string FueraDeTermino  = "fuera_de_termino";
    public const string Ignorada        = "ignorada";

    /// <summary>Estados en los que el administrador todavía puede asignarla a un pedido o ignorarla.</summary>
    public static readonly string[] Pendientes =
        { SinAsignar, Ambigua, ImporteDistinto, FueraDeTermino };
}
