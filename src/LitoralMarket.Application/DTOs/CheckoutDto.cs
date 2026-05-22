using System.ComponentModel.DataAnnotations;

namespace LitoralMarket.Application.DTOs;

public class CheckoutDto
{
    // Validación condicional: obligatorio solo si el usuario NO está autenticado.
    // La validación se realiza en el PageModel para evitar falsos positivos en usuarios logueados.
    [StringLength(200)]
    public string NombreCliente { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Email inválido")]
    [StringLength(150)]
    public string EmailCliente { get; set; } = string.Empty;

    // Obligatorio para usuarios anónimos — validado en el PageModel
    [StringLength(50)]
    public string? TelefonoCliente { get; set; }

    // Dirección de entrega estructurada
    [Required(ErrorMessage = "Seleccioná una opción de entrega")]
    [Range(1, int.MaxValue, ErrorMessage = "Seleccioná una opción de entrega")]
    public int DireccionEntregaId { get; set; }

    // Texto libre, obligatorio solo cuando la opción tiene PermiteLibre = true
    [StringLength(300)]
    public string? DireccionEntregaTexto { get; set; }

    [StringLength(500)]
    public string? Observacion { get; set; }

    public int? ClienteId { get; set; }
}
