using System.ComponentModel.DataAnnotations;

namespace LitoralMarket.Application.DTOs;

public class SeleccionPagoDto
{
    public int PedidoId { get; set; }

    /// <summary>reembolso | mercadopago | transferencia</summary>
    [Required(ErrorMessage = "Seleccioná un método de pago")]
    public string MetodoPago { get; set; } = string.Empty;
}
