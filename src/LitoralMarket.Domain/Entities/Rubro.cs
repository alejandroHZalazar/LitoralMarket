namespace LitoralMarket.Domain.Entities;

public class Rubro
{
    public int Id { get; set; }
    public string? Descripcion { get; set; }

    public ICollection<Producto> Productos { get; set; } = new List<Producto>();
}
