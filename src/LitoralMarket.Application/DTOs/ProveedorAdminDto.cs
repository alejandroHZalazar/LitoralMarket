using System.ComponentModel.DataAnnotations;

namespace LitoralMarket.Application.DTOs;

public class ProveedorAdminDto
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string? NombreComercial { get; set; }

    [MaxLength(20)]
    public string? Cuil { get; set; }

    [MaxLength(300)]
    public string? Direccion { get; set; }

    [MaxLength(150), EmailAddress]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? Telefono { get; set; }

    [MaxLength(50)]
    public string? Celular { get; set; }

    /// <summary>% de ganancia sobre precio proveedor → precio costo.</summary>
    public decimal Ganancia { get; set; }

    /// <summary>% de descuento sobre costo → precio lista.</summary>
    public decimal Descuento { get; set; }

    public bool Baja { get; set; }
}
