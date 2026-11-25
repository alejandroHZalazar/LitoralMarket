namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Datos propios de un cobro de tipo 'transferencia' (1:1 con <see cref="CobroEcommerce"/>).
/// Viven en una tabla aparte a propósito: así cobros_ecommerce, que consulta todo el checkout,
/// no cambia y el código sigue funcionando aunque el script SQL de transferencias todavía no se haya corrido.
/// </summary>
public class CobroTransferencia
{
    /// <summary>cobros_ecommerce.id. Es la clave primaria.</summary>
    public int FkCobro { get; set; }

    /// <summary>Importe exacto que el cliente debe transferir: total del pedido menos entre $0,01 y $0,99.</summary>
    public decimal MontoTransferencia { get; set; }

    /// <summary>Fecha límite para transferir. Pasada, el cobro se cancela y libera su importe.</summary>
    public DateTime Vencimiento { get; set; }

    /// <summary>
    /// Igual a <see cref="MontoTransferencia"/> mientras el cobro está pendiente y NULL en cualquier otro
    /// caso. Tiene índice UNIQUE: la base impide dos cobros pendientes con el mismo importe (NULL se repite).
    /// </summary>
    public decimal? MontoActivo { get; set; }

    public CobroEcommerce? Cobro { get; set; }
}
