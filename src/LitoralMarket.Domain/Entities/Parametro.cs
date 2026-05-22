namespace LitoralMarket.Domain.Entities;

public class Parametro
{
    public int Id { get; set; }
    public string? Modulo { get; set; }
    public string? ParametroNombre { get; set; }
    public string? Valor { get; set; }
    public byte[]? Imagen { get; set; }
}
