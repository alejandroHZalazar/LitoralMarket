namespace LitoralMarket.Application.DTOs;

public class PedidoAdminDto
{
    public int      Id               { get; set; }
    public DateTime Fecha            { get; set; }
    public string   NombreCliente    { get; set; } = string.Empty;
    public string?  EmailCliente     { get; set; }
    public string?  TelefonoCliente  { get; set; }
    public decimal  Total            { get; set; }
    public string?  Estado           { get; set; }
    public string?  DireccionEntrega { get; set; }
    public string?  Observacion      { get; set; }
    public int      CantItems        { get; set; }
    public string?  MetodoPago       { get; set; }

    public List<PedidoDetalleDto> Items { get; set; } = new();
}
