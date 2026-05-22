namespace LitoralMarket.Application.DTOs;

public class PedidoResumenDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string Estado { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal SubtotalProductos { get; set; }
    public decimal CostoEnvio { get; set; }
    public string NombreCliente { get; set; } = string.Empty;
    public string? EmailCliente { get; set; }
    public string? DireccionEntrega { get; set; }
    public string? Observacion { get; set; }

    /// <summary>Tipo de cobro: "reembolso" | "mercadopago" | null si no hay cobro aún.</summary>
    public string? MetodoPago { get; set; }
    /// <summary>Id del cobro activo, usado para generar el PDF.</summary>
    public int? CobroId { get; set; }

    // Campos internos para verificación de propiedad (IDOR) — no se exponen en la UI.
    public int? ClienteId  { get; set; }
    public string? GuestToken { get; set; }

    public List<PedidoDetalleDto> Items { get; set; } = new();
}

public class PedidoDetalleDto
{
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal Precio { get; set; }
    public decimal Subtotal { get; set; }
}
