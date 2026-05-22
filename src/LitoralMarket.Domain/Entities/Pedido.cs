namespace LitoralMarket.Domain.Entities;

public class Pedido
{
    public int Id { get; set; }
    public decimal? Total { get; set; }
    public DateTime? Fecha { get; set; }
    public int? FkCliente { get; set; }
    public decimal? Iva { get; set; }
    public decimal? Recargo { get; set; }
    public decimal? Descuento { get; set; }
    public int? FkVendedor { get; set; }
    public string? Observacion { get; set; }
    public bool? Impreso { get; set; }
    public bool? Vendido { get; set; }
    public bool? EsEcommerce { get; set; }
    public string EstadoEcommerce { get; set; } = "borrador";
    public string? NombreCliente { get; set; }
    public string? EmailCliente { get; set; }
    public string? TelefonoCliente { get; set; }
    public string? DireccionEntrega { get; set; }      // texto descriptivo final
    public int? FkDireccionEntrega { get; set; }       // opción elegida de la tabla
    public decimal? CostoEnvio { get; set; }           // costo sumado al total
    public string? DireccionEntregaTexto { get; set; } // texto libre cuando permiteLibre=1
    public string? GuestToken { get; set; }

    public Cliente? Cliente { get; set; }
    public ICollection<PedidoDetalle> Detalles { get; set; } = new List<PedidoDetalle>();
}
