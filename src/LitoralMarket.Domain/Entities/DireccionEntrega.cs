namespace LitoralMarket.Domain.Entities;

public class DireccionEntrega
{
    public int Id { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string TipoEntrega { get; set; } = "retiro";   // retiro | domicilio | fijo
    public string? Direccion { get; set; }
    public string? Localidad { get; set; }
    public string? Provincia { get; set; }
    public string? CodigoPostal { get; set; }
    public string? Referencia { get; set; }
    public decimal CostoEnvio { get; set; }
    public bool EsGratis { get; set; }
    public bool PermiteLibre { get; set; }  // true → habilita campo de texto libre
    public bool Activo { get; set; } = true;
    public bool EsDefault { get; set; }
    public int Orden { get; set; }
}
