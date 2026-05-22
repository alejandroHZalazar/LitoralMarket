namespace LitoralMarket.Application.DTOs;

public class DireccionEntregaDto
{
    public int Id { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string TipoEntrega { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Localidad { get; set; }
    public string? Referencia { get; set; }
    public decimal CostoEnvio { get; set; }
    public bool EsGratis { get; set; }
    public bool PermiteLibre { get; set; }
    public bool EsDefault { get; set; }

    public string EtiquetaCosto =>
        EsGratis || CostoEnvio == 0 ? "Gratis" : $"+ ${CostoEnvio:N2}";
}
