namespace LitoralMarket.Domain.Entities;

public class Proveedor
{
    public int      Id              { get; set; }
    public string?  NombreComercial { get; set; }
    public string?  Cuil            { get; set; }
    public string?  Direccion       { get; set; }
    public string?  Email           { get; set; }
    public string?  Telefono        { get; set; }
    public string?  Celular         { get; set; }
    public decimal? Ganancia        { get; set; }
    public decimal? Descuento       { get; set; }
    public bool?    Baja            { get; set; }
}
