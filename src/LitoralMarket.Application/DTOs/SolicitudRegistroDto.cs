using System.ComponentModel.DataAnnotations;

namespace LitoralMarket.Application.DTOs;

/// <summary>
/// Datos que un visitante envía desde "Registrarse" (modo Credenciales). Solo se notifica
/// por email a los administradores; todavía no crea el cliente ni genera credenciales.
/// Largos = columnas de la tabla de clientes.
/// </summary>
public class SolicitudRegistroDto
{
    [Required(ErrorMessage = "El nombre comercial es obligatorio."), MaxLength(150)]
    public string? NombreComercial { get; set; }

    [MaxLength(150)] public string? RazonSocial { get; set; }

    [MaxLength(11, ErrorMessage = "El CUIL o DNI admite hasta 11 caracteres.")]
    public string? CuilDni { get; set; }

    [MaxLength(150)] public string? Direccion { get; set; }
    [MaxLength(150)] public string? Localidad { get; set; }

    [Required(ErrorMessage = "El email es obligatorio."),
     EmailAddress(ErrorMessage = "El email no tiene un formato válido."),
     MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(150)] public string? Telefono { get; set; }
    [MaxLength(150)] public string? Celular { get; set; }
}
