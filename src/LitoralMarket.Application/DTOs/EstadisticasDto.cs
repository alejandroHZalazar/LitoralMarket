namespace LitoralMarket.Application.DTOs;

public class EstadisticasDto
{
    // ── KPIs financieros ──────────────────────────────────────────────────
    public int     PedidosConfirmados  { get; set; }
    public decimal TotalVentas         { get; set; }   // SUM(Subtotal) con IVA
    public decimal TotalSinIva         { get; set; }   // SUM(SubtotalSinIva)
    public decimal CostoMercaderia     { get; set; }   // SUM(Costo × Cantidad)
    public decimal GananciaBruta       { get; set; }   // TotalSinIva - CostoMercaderia
    public decimal MargenPct           { get; set; }   // GananciaBruta / TotalSinIva × 100
    public decimal TicketPromedio      { get; set; }   // TotalVentas / PedidosConfirmados

    // ── KPIs operativos ───────────────────────────────────────────────────
    public int PendientePago           { get; set; }
    public int ClientesAutenticados    { get; set; }   // con cuenta, en el período
    public int ClientesAnonimos        { get; set; }   // GuestTokens distintos, en el período
    public int TotalClientesConCuenta  { get; set; }   // histórico total

    // ── Distribución por estado ───────────────────────────────────────────
    public List<EstadoStatDto>   PorEstado    { get; set; } = new();

    // ── Top 10 productos ─────────────────────────────────────────────────
    public List<ProductoStatDto> TopProductos { get; set; } = new();

    // ── Top 10 clientes autenticados ──────────────────────────────────────
    public List<ClienteStatDto>  TopRegistrados { get; set; } = new();

    // ── Top 10 clientes anónimos ──────────────────────────────────────────
    public List<ClienteStatDto>  TopAnonimos    { get; set; } = new();

    // ── Pedidos confirmados por mes (últimos 12 meses) ────────────────────
    public List<MesStatDto>      PorMes         { get; set; } = new();
}

// ── Sub-DTOs ──────────────────────────────────────────────────────────────

public class EstadoStatDto
{
    public string  Estado   { get; set; } = "";
    public int     Cantidad { get; set; }
    public decimal Total    { get; set; }
}

public class ProductoStatDto
{
    public string  Descripcion     { get; set; } = "";
    public decimal CantidadVendida { get; set; }
    public decimal TotalVentas     { get; set; }   // subtotal con IVA
    public decimal CostoTotal      { get; set; }   // Costo × Cantidad
    public decimal GananciaBruta   { get; set; }   // VentasSinIva - CostoTotal
    public decimal MargenPct       { get; set; }   // Ganancia / VentasSinIva × 100
}

public class ClienteStatDto
{
    public string  Nombre          { get; set; } = "";
    public bool    EsAnonimo       { get; set; }
    public int     CantidadPedidos { get; set; }
    public decimal TotalCompras    { get; set; }
}

public class MesStatDto
{
    public string  Label    { get; set; } = "";   // "Ene 2025"
    public int     Cantidad { get; set; }
    public decimal Total    { get; set; }
}
