namespace LitoralMarket.Application.DTOs;

public class ProveedorDto
{
    public int     Id       { get; set; }
    public string  Nombre   { get; set; } = string.Empty;
    public decimal Ganancia { get; set; }
    public decimal Descuento{ get; set; }
}
