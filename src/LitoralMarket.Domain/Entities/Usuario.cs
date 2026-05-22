namespace LitoralMarket.Domain.Entities;

public class Usuario
{
    public int     Id                { get; set; }
    public string? Nombre            { get; set; }
    public string? Password          { get; set; }
    public int?    Tipo              { get; set; }
    public bool?   Baja              { get; set; }
    public string? PasswordHash      { get; set; }
    public bool?   PasswordMigrated  { get; set; }
}
