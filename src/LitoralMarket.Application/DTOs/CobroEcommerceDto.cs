namespace LitoralMarket.Application.DTOs;

public class CobroEcommerceDto
{
    public int Id { get; set; }
    public int FkPedido { get; set; }
    public string Tipo { get; set; } = "reembolso";
    public string Estado { get; set; } = "pendiente";
    public decimal Monto { get; set; }
    public string? Concepto { get; set; }

    // MercadoPago
    public string? MpPreferenceId { get; set; }
    public string? MpLinkPago { get; set; }
    public DateTime? MpFechaExpiracion { get; set; }

    public DateTime FechaCreacion { get; set; }
}
