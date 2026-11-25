namespace LitoralMarket.Application.DTOs;

/// <summary>Medida o color del maestro (variantesMedidas / variantesColores), con cuántas variantes lo usan.</summary>
public class AtributoVarianteDto
{
    public int     Id        { get; set; }
    public string  Nombre    { get; set; } = string.Empty;
    /// <summary>Solo colores: #RRGGBB opcional para mostrar una muestra.</summary>
    public string? CodigoHex { get; set; }
    public int     Orden     { get; set; }
    public bool    Activa    { get; set; } = true;
    /// <summary>Cantidad de variantes que usan este atributo (no se puede eliminar si es &gt; 0; se deshabilita).</summary>
    public int     EnUso     { get; set; }
}

public class ResultadoAtributo
{
    public bool    Ok    { get; set; }
    public string? Error { get; set; }
    public int     Id    { get; set; }
    public static ResultadoAtributo Exito(int id) => new() { Ok = true, Id = id };
    public static ResultadoAtributo Fallo(string error) => new() { Ok = false, Error = error };
}

/// <summary>Una fila de la grilla de variantes (una combinación medida/color).</summary>
public class VarianteFilaDto
{
    /// <summary>Productos.id de la variante. Null = todavía no existe (se crea al guardar).</summary>
    public int?    ProductoId   { get; set; }
    /// <summary>variantesMedidas.id; 0 si la ficha no usa medidas.</summary>
    public int     MedidaId     { get; set; }
    /// <summary>variantesColores.id; 0 si la ficha no usa colores.</summary>
    public int     ColorId      { get; set; }
    public string? MedidaNombre { get; set; }
    public string? ColorNombre  { get; set; }
    public string? ColorHex     { get; set; }
    /// <summary>false = combinación deshabilitada (la variante queda con baja = 1 y no se muestra en la tienda).</summary>
    public bool    Habilitada   { get; set; } = true;
    public string? CodBarras    { get; set; }
    public string? CodProveedor { get; set; }
    /// <summary>
    /// Costo propio. Null = sin valor propio: una variante nueva usa el costo común (si los precios son iguales)
    /// y una existente no se toca.
    /// </summary>
    public decimal? Costo       { get; set; }
    /// <summary>Precio de venta propio. Misma regla que <see cref="Costo"/>.</summary>
    public decimal? Precio      { get; set; }
    /// <summary>Solo variantes nuevas: cantidad inicial opcional (se registra como ingreso de stock).</summary>
    public decimal  StockInicial { get; set; }
    /// <summary>Solo lectura: stock actual de la variante.</summary>
    public decimal  Stock       { get; set; }
    /// <summary>Solo lectura: con precios iguales, true si el costo o el precio de la variante difieren del común.</summary>
    public bool     PrecioPropio { get; set; }
    public int      Orden       { get; set; }
    /// <summary>Solo lectura: descripción generada ("NOMBRE - MEDIDA - COLOR").</summary>
    public string?  Descripcion { get; set; }
}

/// <summary>Estado de variantes de un producto, para cargar el editor.</summary>
public class FichaVariantesDto
{
    public int  ProductoId     { get; set; }
    public bool TieneVariantes { get; set; }
    public bool UsaMedidas     { get; set; }
    public bool UsaColores     { get; set; }
    public bool PreciosIguales { get; set; } = true;
    public List<VarianteFilaDto> Variantes { get; set; } = new();
}

/// <summary>Cómo propagar un cambio del costo/precio común (con precios iguales) a las variantes existentes.</summary>
public static class ModoPropagacionPrecio
{
    /// <summary>No se toca ninguna variante existente.</summary>
    public const int Ninguna = 0;
    /// <summary>Solo las variantes cuyo costo/precio actual es igual al común anterior (siguen al común).</summary>
    public const int SoloQuienesSiguen = 1;
    /// <summary>Todas las variantes pasan a tener el costo y el precio comunes.</summary>
    public const int Todas = 2;
}

public class GuardarVariantesRequest
{
    public bool UsaMedidas     { get; set; }
    public bool UsaColores     { get; set; }
    public bool PreciosIguales { get; set; } = true;
    /// <summary>Ver <see cref="ModoPropagacionPrecio"/>.</summary>
    public int  ModoPropagacion { get; set; }
    public List<VarianteFilaDto> Variantes { get; set; } = new();
}

public class ResultadoVariantes
{
    public bool         Ok         { get; set; }
    public List<string> Errores    { get; set; } = new();
    public int          ProductoId { get; set; }
    public int Creadas        { get; set; }
    public int Actualizadas   { get; set; }
    public int Habilitadas    { get; set; }
    public int Deshabilitadas { get; set; }
    /// <summary>Variantes existentes que recibieron el nuevo costo/precio común.</summary>
    public int PreciosPropagados { get; set; }
    /// <summary>Variantes con precio propio a las que NO se les cambió el costo/precio.</summary>
    public int PreciosOmitidos   { get; set; }
    /// <summary>Si alguna variante nueva pidió stock inicial y el ingreso falló (las variantes sí quedaron creadas).</summary>
    public string? AvisoStockInicial { get; set; }
    /// <summary>Códigos automáticos asignados a variantes nuevas (productos/generaCodigoBarras = '1').</summary>
    public List<string> CodigosAsignados { get; set; } = new();

    public static ResultadoVariantes Fallo(IEnumerable<string> errores) =>
        new() { Ok = false, Errores = errores.ToList() };
}

/// <summary>Qué pasaría al cambiar el costo/precio común de una ficha (para confirmar antes de guardar).</summary>
public class VistaPreviaPreciosDto
{
    /// <summary>Variantes que siguen al precio común (se actualizarían).</summary>
    public int Siguen { get; set; }
    /// <summary>Variantes con precio propio (no se modificarían).</summary>
    public int ConPrecioPropio { get; set; }
    /// <summary>Descripciones de las variantes con precio propio (hasta 20).</summary>
    public List<string> Propias { get; set; } = new();
}

/// <summary>Una variante tal como se muestra en el detalle del catálogo (todas viajan en la página).</summary>
public class VarianteCatalogoDto
{
    /// <summary>Productos.id de la variante: es lo que se envía al carrito.</summary>
    public int     Id          { get; set; }
    public int     MedidaId    { get; set; }
    public string? Medida      { get; set; }
    public int     ColorId     { get; set; }
    public string? Color       { get; set; }
    public string? ColorHex    { get; set; }
    public decimal Precio      { get; set; }
    public decimal Stock       { get; set; }
    public string? CodBarras   { get; set; }
    public string? CodProveedor { get; set; }
    /// <summary>Mayor cantidad que se puede pedir según el stock (solo con ecommerce/LimitarCantidadPorStock = 1).</summary>
    public decimal? MaximoPermitido { get; set; }

    // Promociones por cantidad / porcentaje de ESTA variante (promociones/descuentos = 1; apagado quedan sin valor)
    public decimal? PrecioPromo { get; set; }
    public decimal? PromoPorcentaje { get; set; }
    public int? PromoLleva { get; set; }
    public int? PromoPaga { get; set; }
    public bool PromoAgrupa { get; set; }
}

/// <summary>Datos de una ficha con variantes para el detalle del catálogo.</summary>
public class FichaCatalogoDto
{
    public int  PrincipalId { get; set; }
    public bool UsaMedidas  { get; set; }
    public bool UsaColores  { get; set; }
    public List<VarianteCatalogoDto> Variantes { get; set; } = new();
}
