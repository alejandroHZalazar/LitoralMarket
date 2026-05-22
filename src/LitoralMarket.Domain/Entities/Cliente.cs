namespace LitoralMarket.Domain.Entities;

public class Cliente
{
    public int Id { get; set; }
    public string? NombreComercial { get; set; }
    public string? RazonSocial { get; set; }
    public string? Cuil { get; set; }
    public string? Direccion { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    public string? Celular { get; set; }
    public string? Contacto { get; set; }
    public int? FkCondIva { get; set; }
    public int? FkVendedor { get; set; }
    public bool? Baja { get; set; }
    public int? FkLocalidad { get; set; }
    public int? FkZona { get; set; }
    public decimal? SaldoCuentaCorriente { get; set; }
    public string? PasswordHash { get; set; }
    public bool? EmailConfirmado { get; set; }

    public ICollection<Pedido> Pedidos { get; set; } = new List<Pedido>();
}
