namespace LitoralMarket.Application.DTOs;

/// <summary>Estado de una promocion como destino de un banner o de la pagina de promocion.</summary>
public enum EstadoPromoDestino
{
    /// <summary>Activa y dentro de su periodo (hora Argentina): se pueden listar sus productos.</summary>
    Vigente,
    NoExiste,
    Inactiva,
    Vencida,
    /// <summary>Activa pero todavia no comenzo.</summary>
    Futura,
    /// <summary>El modulo de promociones esta apagado (parametro promociones/descuentos) o faltan sus tablas.</summary>
    Deshabilitada
}

/// <summary>Datos de una promocion para la pagina de destino del carrusel (sin consultar el catalogo).</summary>
public class PromoDestinoDto
{
    public int                 Id         { get; set; }
    public EstadoPromoDestino  Estado     { get; set; }
    public string              Nombre     { get; set; } = string.Empty;
    public string              Tipo       { get; set; } = string.Empty;   // PORC | NXM
    /// <summary>"20 % de descuento" / "Llevá 3, pagá 2".</summary>
    public string              Beneficio  { get; set; } = string.Empty;
    /// <summary>Condicion de una promocion por cantidad ("Sumá 3 unidades combinando variantes"); null en porcentaje.</summary>
    public string?             Condicion  { get; set; }
    public DateTime            Desde      { get; set; }
    public DateTime            Hasta      { get; set; }
    public List<int>           ProductoIds { get; set; } = new();
}
