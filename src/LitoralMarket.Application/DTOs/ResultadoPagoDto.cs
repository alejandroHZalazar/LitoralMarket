namespace LitoralMarket.Application.DTOs;

public class ResultadoPagoDto
{
    public bool Exito { get; set; }
    public string? Error { get; set; }
    public int PedidoId { get; set; }
    public int CobroId { get; set; }
    public string Tipo { get; set; } = "reembolso";

    // MercadoPago
    public string? MpLinkPago { get; set; }
    public DateTime? MpFechaExpiracion { get; set; }

    // Email
    public bool EmailEnviado { get; set; }
}
