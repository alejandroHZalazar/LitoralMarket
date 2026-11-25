namespace LitoralMarket.Domain.Entities;

/// <summary>
/// Destino de un banner del carrusel: la promocion por cantidad / porcentaje (promociones_reglas.id) a cuyos productos
/// lleva el clic. Tabla promocionesCarruselDestinos (un destino por banner). Sin FK hacia la regla: puede eliminarse.
/// </summary>
public class PromocionCarruselDestino
{
    public int FkPromocionCarrusel { get; set; }
    public int FkPromocionRegla    { get; set; }
}
