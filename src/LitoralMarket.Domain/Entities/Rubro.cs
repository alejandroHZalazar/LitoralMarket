namespace LitoralMarket.Domain.Entities;

public class Rubro
{
    public int Id { get; set; }
    public string? Descripcion { get; set; }

    /// <summary>Rubro padre (Rubros.fk_rubroPadre). null = rubro principal.</summary>
    public int? FkRubroPadre { get; set; }

    public ICollection<Producto> Productos { get; set; } = new List<Producto>();
}
