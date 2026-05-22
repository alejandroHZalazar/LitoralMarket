namespace LitoralMarket.Domain.Entities;

public class CobroEcommerce
{
    public int Id { get; set; }
    public int FkPedido { get; set; }

    /// <summary>reembolso | mercadopago</summary>
    public string Tipo { get; set; } = "reembolso";

    /// <summary>pendiente | aprobado | rechazado | cancelado</summary>
    public string Estado { get; set; } = "pendiente";

    public decimal Monto { get; set; }
    public string? Concepto { get; set; }

    // MercadoPago
    public string? MpPreferenceId { get; set; }
    public string? MpPaymentId { get; set; }
    public string? MpLinkPago { get; set; }
    public DateTime? MpFechaExpiracion { get; set; }
    public string? MpStatus { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.Now;
    public DateTime? FechaPago { get; set; }

    // Navegación
    public Pedido? Pedido { get; set; }
}
