namespace LitoralMarket.Application.Promociones;

// Port LITERAL del motor de promociones de Comercial Web (Domain/Promociones/MotorPromociones.cs), que a su vez es el del
// sistema de escritorio (ClassMotorPromociones.cs). NO modificar las reglas aca sin cambiarlas tambien alla: las tres
// aplicaciones tienen que calcular exactamente lo mismo (incluida la acumulacion de variantes de una ficha).


/// <summary>Tipos de regla soportados por el motor de promociones por cantidad / porcentaje.</summary>
public static class TipoPromo
{
    public const string Porcentaje = "PORC";
    public const string NxM        = "NXM";
}

/// <summary>Regla de promoción ya cargada en memoria (sin acceso a datos).</summary>
public class ReglaPromo
{
    public int      Id         { get; set; }
    public string   Nombre     { get; set; } = string.Empty;
    public string   Tipo       { get; set; } = TipoPromo.Porcentaje;
    public decimal? Porcentaje { get; set; }
    public int?     Lleva      { get; set; }
    public int?     Paga       { get; set; }
    public DateTime Desde      { get; set; }
    public DateTime Hasta      { get; set; }
    public bool     Activa     { get; set; } = true;
    /// <summary>
    /// Solo NxM: las cantidades de las distintas variantes de una misma ficha (que estén en la lista de productos de la
    /// regla) se acumulan para completar el grupo. false (por defecto) = cada producto se evalúa por separado.
    /// </summary>
    public bool     AgrupaVariantes { get; set; }
    public HashSet<int> ProductoIds { get; set; } = new();

    /// <summary>Vigente = activa y 'ahora' dentro de [Desde, Hasta] (inclusive en ambos extremos).</summary>
    public bool EstaVigente(DateTime ahora) => Activa && ahora >= Desde && ahora <= Hasta;
}

/// <summary>Línea de entrada al motor. 'Clave' es un identificador opaco que el motor devuelve intacto.</summary>
public class LineaPromoEntrada
{
    public int     Clave        { get; set; }
    public int     ProductoId   { get; set; }
    public decimal Cantidad     { get; set; }
    public decimal PrecioSinIva { get; set; }
    /// <summary>Ficha (producto principal) a la que pertenece la variante; null si el producto no es una variante.</summary>
    public int?    FichaId      { get; set; }
}

/// <summary>Promoción aplicada a una línea.</summary>
public class LineaPromoResultado
{
    public int      Clave          { get; set; }
    public int      ProductoId     { get; set; }
    public int      ReglaId        { get; set; }
    public string   Nombre         { get; set; } = string.Empty;
    public string   Tipo           { get; set; } = string.Empty;
    public decimal? Porcentaje     { get; set; }
    public int?     Lleva          { get; set; }
    public int?     Paga           { get; set; }
    /// <summary>Unidades bonificadas en ESTA línea (solo NxM; 0 para porcentaje).</summary>
    public decimal  CantBonificada { get; set; }
    /// <summary>Descuento efectivo de la línea en % (0-100): lo que debe verse en la columna Desc/Rec.</summary>
    public decimal  DescuentoPct   { get; set; }
    /// <summary>Importe sin IVA que se deja de cobrar en esta línea.</summary>
    public decimal  Ahorro         { get; set; }
    /// <summary>La regla aplicada acumula las variantes de una misma ficha (solo informativo).</summary>
    public bool     AgrupaVariantes { get; set; }
}

/// <summary>
/// Motor puro (sin base de datos, sin hora del sistema) que decide qué promoción aplicar a cada producto.
///
/// Reglas:
///   * Se agrupan las líneas por producto y se evalúa la CANTIDAD TOTAL del producto.
///   * Candidatas: reglas vigentes que incluyen el producto y cuyas condiciones se cumplen
///     (NxM requiere cantidad &gt;= N; porcentaje siempre).
///   * Importe a pagar (en unidades de precio): PORC = q × (1 − %/100); NxM = q − grupos × (N − M),
///     con grupos = floor(q / N). Las unidades que no completan un grupo conservan su precio.
///   * Se elige la candidata de MENOR importe final. No se acumulan promociones: una sola por producto,
///     por lo que nunca hay dos promociones sobre las mismas unidades.
///   * Desempate determinístico: a igual importe gana la regla de menor Id (la más antigua).
///   * Si un producto tiene varias líneas, las unidades bonificadas (NxM) se asignan en orden de línea.
///
/// Acumulación de variantes (regla NxM con AgrupaVariantes):
///   * Solo afecta a las variantes de una ficha (LineaPromoEntrada.FichaId) cuyo producto esté en la regla. Para esas
///     líneas, las cantidades de las variantes de la MISMA ficha y presentes en la lista de la regla se suman para
///     completar el grupo; nunca se mezclan fichas distintas ni variantes que no estén en la promoción.
///   * Las unidades bonificadas se reparten entre las líneas del conjunto en orden de línea (igual que hoy).
///   * Una regla que acumula solo se usa si su ahorro supera al de las ofertas individuales de las mismas líneas
///     (mayor beneficio neto para el cliente; desempate: menor Id de regla). Una línea nunca recibe dos promociones.
///   * Sin reglas con AgrupaVariantes (o sin fichas) el cálculo es exactamente el de siempre.
/// </summary>
public static class MotorPromociones
{
    public static List<LineaPromoResultado> Evaluar(
        IEnumerable<LineaPromoEntrada> lineas, IEnumerable<ReglaPromo> reglas, DateTime ahora)
    {
        var resultado = new List<LineaPromoResultado>();
        var vigentes  = reglas.Where(r => r.EstaVigente(ahora)).OrderBy(r => r.Id).ToList();
        if (vigentes.Count == 0) return resultado;

        var validas = lineas.Where(l => l.Cantidad > 0 && l.PrecioSinIva > 0 && l.ProductoId > 0).ToList();
        var (simples, componentes) = Particionar(validas, vigentes);

        // Camino de siempre: cada producto se evalúa por separado
        foreach (var grupo in simples.GroupBy(l => l.ProductoId))
        {
            var prodLineas = grupo.ToList();
            decimal q = prodLineas.Sum(l => l.Cantidad);

            var (mejor, mejorBon) = Mejor(grupo.Key, q, vigentes);
            if (mejor == null) continue;

            // Reparto entre las líneas del producto
            decimal bonifRestante = mejorBon;
            foreach (var l in prodLineas)
            {
                decimal bonifLinea = 0m, pct, ahorro;
                if (mejor.Tipo == TipoPromo.NxM)
                {
                    bonifLinea     = Math.Min(l.Cantidad, bonifRestante);
                    bonifRestante -= bonifLinea;
                    pct    = l.Cantidad > 0 ? Math.Round(bonifLinea / l.Cantidad * 100m, 8) : 0m;
                    ahorro = bonifLinea * l.PrecioSinIva;
                }
                else
                {
                    pct    = mejor.Porcentaje!.Value;
                    ahorro = l.Cantidad * l.PrecioSinIva * pct / 100m;
                }

                if (pct <= 0m) continue;         // línea sin beneficio (p. ej. NxM cuyo grupo cae en otra línea)

                resultado.Add(new LineaPromoResultado
                {
                    Clave          = l.Clave,
                    ProductoId     = l.ProductoId,
                    ReglaId        = mejor.Id,
                    Nombre         = mejor.Nombre,
                    Tipo           = mejor.Tipo,
                    Porcentaje     = mejor.Tipo == TipoPromo.Porcentaje ? mejor.Porcentaje : null,
                    Lleva          = mejor.Tipo == TipoPromo.NxM ? mejor.Lleva : null,
                    Paga           = mejor.Tipo == TipoPromo.NxM ? mejor.Paga  : null,
                    CantBonificada = bonifLinea,
                    DescuentoPct   = pct,
                    Ahorro         = ahorro
                });
            }
        }

        // Variantes de una ficha alcanzadas por alguna regla NxM que acumula variantes
        foreach (var comp in componentes)
            resultado.AddRange(ResolverComponente(comp, vigentes));

        return resultado;
    }

    /// <summary>
    /// Mejor regla para UN producto con la cantidad total q (la de menor importe final; a igualdad, la de menor Id,
    /// porque 'vigentes' viene ordenada por Id). Devuelve también las unidades bonificadas (solo NxM).
    /// </summary>
    private static (ReglaPromo? Regla, decimal Bonificadas) Mejor(int productoId, decimal q, IReadOnlyList<ReglaPromo> vigentes)
    {
        ReglaPromo? mejor    = null;
        decimal     mejorPag = q;            // unidades "equivalentes" a pagar; solo aplica si es MENOR que q
        decimal     mejorBon = 0m;

        foreach (var r in vigentes)          // orden por Id: el empate lo gana la primera encontrada
        {
            if (!r.ProductoIds.Contains(productoId)) continue;

            decimal aPagar, bonif = 0m;
            if (r.Tipo == TipoPromo.NxM)
            {
                if (!NxmValida(r)) continue;
                if (q < r.Lleva!.Value) continue;
                decimal grupos = Math.Floor(q / r.Lleva!.Value);
                bonif  = grupos * (r.Lleva!.Value - r.Paga!.Value);
                aPagar = q - bonif;
            }
            else if (r.Tipo == TipoPromo.Porcentaje)
            {
                if (!r.Porcentaje.HasValue || r.Porcentaje.Value <= 0m || r.Porcentaje.Value > 100m) continue;
                aPagar = q * (1m - r.Porcentaje.Value / 100m);
            }
            else continue;

            if (aPagar < mejorPag)           // estrictamente menor: a igualdad se conserva la de menor Id
            {
                mejor = r; mejorPag = aPagar; mejorBon = bonif;
            }
        }
        return (mejor, mejorBon);
    }

    private static bool NxmValida(ReglaPromo r)
        => r.Tipo == TipoPromo.NxM && r.Lleva.HasValue && r.Paga.HasValue && r.Lleva.Value >= 2 &&
           r.Paga.Value >= 1 && r.Paga.Value < r.Lleva.Value;

    /// <summary>Regla NxM válida que acumula las variantes de una misma ficha.</summary>
    private static bool EsNxmAgrupada(ReglaPromo r) => r.AgrupaVariantes && NxmValida(r);

    // ═══════════════════════════════════════════════════════════════════════
    // Acumulación de variantes por ficha
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Separa las líneas "simples" (camino de siempre) de los conjuntos por ficha. Una línea va a un conjunto solo si es
    /// variante de una ficha Y su producto está en alguna regla NxM vigente que acumula variantes.
    /// Sin reglas que acumulen todo queda en "simples" (comportamiento idéntico al anterior).
    /// </summary>
    private static (List<LineaPromoEntrada> Simples, List<List<LineaPromoEntrada>> Componentes) Particionar(
        List<LineaPromoEntrada> validas, List<ReglaPromo> vigentes)
    {
        var agrupadas = vigentes.Where(EsNxmAgrupada).ToList();
        if (agrupadas.Count == 0) return (validas, new List<List<LineaPromoEntrada>>());

        var simples    = new List<LineaPromoEntrada>();
        var porFicha   = new Dictionary<int, List<LineaPromoEntrada>>();
        var ordenFicha = new List<int>();
        foreach (var l in validas)
        {
            if (l.FichaId.HasValue && agrupadas.Any(r => r.ProductoIds.Contains(l.ProductoId)))
            {
                if (!porFicha.TryGetValue(l.FichaId.Value, out var lista))
                {
                    porFicha[l.FichaId.Value] = lista = new List<LineaPromoEntrada>();
                    ordenFicha.Add(l.FichaId.Value);
                }
                lista.Add(l);
            }
            else simples.Add(l);
        }
        return (simples, ordenFicha.Select(f => porFicha[f]).ToList());
    }

    private sealed class Oferta
    {
        public ReglaPromo Regla { get; init; } = null!;
        public List<LineaPromoEntrada> Alcance { get; init; } = new();
        public List<LineaPromoResultado> Resultados { get; init; } = new();
        public decimal Ahorro { get; init; }
    }

    /// <summary>Beneficio de una regla sobre un conjunto de líneas (la cantidad que cuenta es la suma del conjunto).</summary>
    private static Oferta? Calcular(ReglaPromo r, List<LineaPromoEntrada> alcance)
    {
        if (alcance.Count == 0) return null;
        decimal q = alcance.Sum(l => l.Cantidad);
        decimal bonifTotal = 0m;

        if (r.Tipo == TipoPromo.NxM)
        {
            if (!NxmValida(r) || q < r.Lleva!.Value) return null;
            bonifTotal = Math.Floor(q / r.Lleva!.Value) * (r.Lleva!.Value - r.Paga!.Value);
        }
        else if (r.Tipo == TipoPromo.Porcentaje)
        {
            if (!r.Porcentaje.HasValue || r.Porcentaje.Value <= 0m || r.Porcentaje.Value > 100m) return null;
        }
        else return null;

        var res = new List<LineaPromoResultado>();
        decimal ahorroTotal = 0m, restante = bonifTotal;
        foreach (var l in alcance)
        {
            decimal bonifLinea = 0m, pct, ahorro;
            if (r.Tipo == TipoPromo.NxM)
            {
                bonifLinea = Math.Min(l.Cantidad, restante);
                restante  -= bonifLinea;
                pct    = l.Cantidad > 0 ? Math.Round(bonifLinea / l.Cantidad * 100m, 8) : 0m;
                ahorro = bonifLinea * l.PrecioSinIva;
            }
            else
            {
                pct    = r.Porcentaje!.Value;
                ahorro = l.Cantidad * l.PrecioSinIva * pct / 100m;
            }
            if (pct <= 0m) continue;

            res.Add(new LineaPromoResultado
            {
                Clave = l.Clave, ProductoId = l.ProductoId, ReglaId = r.Id, Nombre = r.Nombre, Tipo = r.Tipo,
                Porcentaje = r.Tipo == TipoPromo.Porcentaje ? r.Porcentaje : null,
                Lleva = r.Tipo == TipoPromo.NxM ? r.Lleva : null,
                Paga  = r.Tipo == TipoPromo.NxM ? r.Paga  : null,
                CantBonificada = bonifLinea, DescuentoPct = pct, Ahorro = ahorro,
                AgrupaVariantes = EsNxmAgrupada(r)
            });
            ahorroTotal += ahorro;
        }
        if (res.Count == 0) return null;
        return new Oferta { Regla = r, Alcance = alcance, Resultados = res, Ahorro = ahorroTotal };
    }

    /// <summary>
    /// Resuelve las líneas de UNA ficha. Base: cada producto recibe su mejor oferta individual (reglas que no acumulan),
    /// exactamente como siempre. Una regla NxM que acumula variantes se aplica sobre todas las líneas libres de la ficha
    /// cuyo producto está en la regla SOLO si su ahorro supera lo que esas mismas líneas ahorrarían con sus ofertas
    /// individuales (mayor beneficio neto para el cliente). Se toma, de a una, la de mayor beneficio neto (desempate: menor
    /// Id de regla) y se repite con las líneas que quedan libres: una línea nunca recibe dos promociones.
    /// </summary>
    private static List<LineaPromoResultado> ResolverComponente(List<LineaPromoEntrada> lineas, List<ReglaPromo> vigentes)
    {
        var res       = new List<LineaPromoResultado>();
        var libres    = lineas.ToList();
        var agrupadas = vigentes.Where(EsNxmAgrupada).ToList();
        var propias   = vigentes.Where(r => !EsNxmAgrupada(r)).ToList();

        // Mejor oferta individual (reglas que no acumulan) de cada producto con líneas libres
        Dictionary<int, Oferta?> OfertasIndividuales()
        {
            var d = new Dictionary<int, Oferta?>();
            foreach (var g in libres.GroupBy(l => l.ProductoId))
            {
                Oferta? mejorProd = null;
                var lineasProd = g.ToList();
                foreach (var r in propias)
                {
                    if (!r.ProductoIds.Contains(g.Key)) continue;
                    var o = Calcular(r, lineasProd);
                    if (o != null && (mejorProd == null || o.Ahorro > mejorProd.Ahorro ||
                                      (o.Ahorro == mejorProd.Ahorro && o.Regla.Id < mejorProd.Regla.Id)))
                        mejorProd = o;
                }
                d[g.Key] = mejorProd;
            }
            return d;
        }

        while (libres.Count > 0)
        {
            var individuales = OfertasIndividuales();

            Oferta? elegida = null;
            decimal mejorGanancia = 0m;
            foreach (var r in agrupadas)
            {
                var alcance = libres.Where(l => r.ProductoIds.Contains(l.ProductoId)).ToList();
                var o = Calcular(r, alcance);
                if (o == null) continue;
                decimal perdido = alcance.Select(l => l.ProductoId).Distinct()
                    .Sum(pid => individuales.TryGetValue(pid, out var oi) && oi != null ? oi.Ahorro : 0m);
                decimal ganancia = o.Ahorro - perdido;
                if (ganancia > mejorGanancia ||
                    (ganancia == mejorGanancia && ganancia > 0m && elegida != null && r.Id < elegida.Regla.Id))
                {
                    elegida = o; mejorGanancia = ganancia;
                }
            }

            if (elegida == null)                     // ninguna acumulación mejora lo individual: queda lo de siempre
            {
                foreach (var oi in individuales.Values)
                    if (oi != null) res.AddRange(oi.Resultados);
                break;
            }

            res.AddRange(elegida.Resultados);
            var tomadas = elegida.Alcance.ToHashSet();
            libres.RemoveAll(l => tomadas.Contains(l));
        }
        return res;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Avisos de proximidad
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Avisos de proximidad (solo informativos, no modifican importes): productos a UNA unidad de completar un grupo NxM.
    ///   * Se evalúa la cantidad total del producto (q) y la cantidad q + 1 con el mismo criterio de selección que Evaluar.
    ///   * Hay aviso solo si con q + 1 la ganadora es una NxM que bonifica MÁS unidades que con q y q + 1 completa un
    ///     grupo ((q + 1) múltiplo de N). Así nunca se anuncia un beneficio que el motor no aplicaría (por ejemplo,
    ///     cuando otra promoción mejor seguiría ganando) ni se avisa si el beneficio ya fue alcanzado.
    ///   * Solo con cantidades enteras: con decimales la unidad que falta no sería exactamente una.
    ///   * Con varias líneas del mismo producto se avisa sobre la primera línea.
    ///   * En reglas que acumulan variantes la cantidad es la suma de la ficha y el aviso se asocia a la primera línea
    ///     del conjunto (la unidad que falta puede ser de cualquier variante incluida en la promoción).
    /// </summary>
    public static List<LineaPromoProxima> Proximas(
        IEnumerable<LineaPromoEntrada> lineas, IEnumerable<ReglaPromo> reglas, DateTime ahora)
    {
        var res = new List<LineaPromoProxima>();
        var vigentes = reglas.Where(r => r.EstaVigente(ahora)).OrderBy(r => r.Id).ToList();
        if (vigentes.Count == 0) return res;

        var validas = lineas.Where(l => l.Cantidad > 0 && l.PrecioSinIva > 0 && l.ProductoId > 0).ToList();
        var (simples, componentes) = Particionar(validas, vigentes);

        foreach (var grupo in simples.GroupBy(l => l.ProductoId))
        {
            decimal q = grupo.Sum(l => l.Cantidad);
            if (q != Math.Floor(q)) continue;

            var (actual, bonActual) = Mejor(grupo.Key, q, vigentes);
            var (sig, bonSig)       = Mejor(grupo.Key, q + 1m, vigentes);
            if (sig == null || sig.Tipo != TipoPromo.NxM) continue;
            if (bonSig <= (actual == null ? 0m : bonActual)) continue;
            if ((q + 1m) % sig.Lleva!.Value != 0m) continue;

            res.Add(new LineaPromoProxima
            {
                Clave = grupo.First().Clave, ProductoId = grupo.Key, ReglaId = sig.Id, Nombre = sig.Nombre,
                Lleva = sig.Lleva.Value, Paga = sig.Paga!.Value, CantidadActual = q
            });
        }

        foreach (var comp in componentes)
            res.AddRange(ProximasComponente(comp, vigentes));
        return res;
    }

    private static List<LineaPromoProxima> ProximasComponente(List<LineaPromoEntrada> comp, List<ReglaPromo> vigentes)
    {
        var actual = ResolverComponente(comp, vigentes);
        var candidatos = new List<(LineaPromoProxima Aviso, decimal Ahorro, HashSet<int> Productos)>();

        foreach (var r in vigentes.Where(NxmValida))
        {
            var enRegla = comp.Where(l => r.ProductoIds.Contains(l.ProductoId)).ToList();
            if (enRegla.Count == 0) continue;
            var alcances = EsNxmAgrupada(r)
                ? new List<List<LineaPromoEntrada>> { enRegla }
                : enRegla.GroupBy(l => l.ProductoId).Select(g => g.ToList()).ToList();

            foreach (var alcance in alcances)
            {
                decimal q = alcance.Sum(l => l.Cantidad);
                if (q != Math.Floor(q) || (q + 1m) % r.Lleva!.Value != 0m) continue;

                var primera = alcance[0];
                var simulado = comp.Select(l => ReferenceEquals(l, primera)
                    ? new LineaPromoEntrada { Clave = l.Clave, ProductoId = l.ProductoId, Cantidad = l.Cantidad + 1m,
                                              PrecioSinIva = l.PrecioSinIva, FichaId = l.FichaId }
                    : l).ToList();
                var conUnidad = ResolverComponente(simulado, vigentes);

                var claves = alcance.Select(l => l.Clave).ToHashSet();
                var propias = conUnidad.Where(x => x.ReglaId == r.Id && claves.Contains(x.Clave)).ToList();
                decimal bonifNueva  = propias.Sum(x => x.CantBonificada);
                decimal bonifActual = actual.Where(x => x.Tipo == TipoPromo.NxM && claves.Contains(x.Clave)).Sum(x => x.CantBonificada);
                if (bonifNueva <= 0m || bonifNueva <= bonifActual) continue;

                candidatos.Add((new LineaPromoProxima
                {
                    Clave = primera.Clave, ProductoId = primera.ProductoId, ReglaId = r.Id, Nombre = r.Nombre,
                    Lleva = r.Lleva.Value, Paga = r.Paga!.Value, CantidadActual = q, Agrupa = EsNxmAgrupada(r)
                }, propias.Sum(x => x.Ahorro), alcance.Select(l => l.ProductoId).ToHashSet()));
            }
        }

        // Un aviso por producto: se conserva el de mayor ahorro (desempate: menor Id de regla)
        var elegidos = new List<LineaPromoProxima>();
        var tomados  = new HashSet<int>();
        foreach (var c in candidatos.OrderByDescending(c => c.Ahorro).ThenBy(c => c.Aviso.ReglaId))
        {
            if (c.Productos.Overlaps(tomados)) continue;
            elegidos.Add(c.Aviso);
            tomados.UnionWith(c.Productos);
        }
        return elegidos;
    }
}

/// <summary>Producto que está a una unidad de completar un grupo NxM (aviso informativo).</summary>
public class LineaPromoProxima
{
    public int     Clave          { get; set; }
    public int     ProductoId     { get; set; }
    public int     ReglaId        { get; set; }
    public string  Nombre         { get; set; } = string.Empty;
    public int     Lleva          { get; set; }
    public int     Paga           { get; set; }
    /// <summary>Cantidad total actual (del producto o, con acumulación de variantes, de la ficha).</summary>
    public decimal CantidadActual { get; set; }
    /// <summary>La unidad que falta puede ser de cualquier variante de la ficha incluida en la promoción.</summary>
    public bool    Agrupa         { get; set; }
}
