using System.Globalization;
using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Helpers;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Application.Promociones;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Promociones por cantidad (NxM) y porcentaje: orquesta el acceso a datos alrededor del motor puro
/// <see cref="MotorPromociones"/> (port literal del de Comercial Web). Mismas tablas y mismas reglas que Comercial Web.
/// </summary>
public class PromocionesService : IPromocionesService
{
    private const string Modulo    = "promociones";
    private const string Parametro = "descuentos";
    private const string KeyEstado = "promo_estado";
    private const string KeyReglas = "promo_reglas";
    private static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(60);
    private static readonly CultureInfo Ar = new("es-AR");

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IParametrosService _parametros;

    public PromocionesService(AppDbContext db, IMemoryCache cache, IParametrosService parametros)
    {
        _db = db;
        _cache = cache;
        _parametros = parametros;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Estado (parametro + tablas), cacheado: sin consultas por operacion
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<bool> HabilitadoAsync()   => (await EstadoAsync()).Habilitado;
    public async Task<bool> TablasListasAsync() => (await EstadoAsync()).Tablas;

    private async Task<(bool Habilitado, bool Tablas)> EstadoAsync()
    {
        if (_cache.TryGetValue(KeyEstado, out (bool, bool) e)) return e;

        var valor = await _db.Parametros.AsNoTracking()
            .Where(p => p.Modulo == Modulo && p.ParametroNombre == Parametro)
            .Select(p => p.Valor)
            .FirstOrDefaultAsync();

        // Tablas del modulo + columna agrupaVariantes (si falta, el modulo queda inerte en vez de fallar al consultar).
        var tablas = await ContarTablasAsync() == 4 && await ColumnaAgrupaVariantesExisteAsync();
        var estado = (tablas && valor?.Trim() == "1", tablas);
        _cache.Set(KeyEstado, estado, Vigencia);
        return estado;
    }

    protected virtual async Task<bool> ColumnaAgrupaVariantesExisteAsync()
        => (await _db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS `Value` FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) = 'promociones_reglas' AND LOWER(COLUMN_NAME) = 'agrupavariantes'")
            .ToListAsync()).FirstOrDefault() == 1;

    /// <summary>Costura para las pruebas (SQLite no tiene information_schema).</summary>
    protected virtual async Task<int> ContarTablasAsync()
        => (await _db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS `Value` FROM information_schema.TABLES " +
            "WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) IN " +
            "('promociones_reglas','promociones_reglas_productos','ventas_promo_detalle','pedidos_promo_detalle')")
            .ToListAsync()).FirstOrDefault();

    // ═══════════════════════════════════════════════════════════════════════
    // Reglas (cacheadas): activas y no vencidas, con sus productos
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<List<ReglaPromo>> ReglasAsync()
    {
        if (_cache.TryGetValue(KeyReglas, out List<ReglaPromo>? cacheadas) && cacheadas is not null) return cacheadas;

        var ahora  = HoraArgentina.Ahora();
        var reglas = await _db.PromocionesReglas.AsNoTracking()
            .Where(r => r.Activa && r.VigenteHasta >= ahora).ToListAsync();

        var porRegla = new Dictionary<int, HashSet<int>>();
        if (reglas.Count > 0)
        {
            var ids = reglas.Select(r => r.Id).ToList();
            var asoc = await _db.PromocionesReglasProductos.AsNoTracking()
                .Where(p => ids.Contains(p.FkRegla))
                .Select(p => new { p.FkRegla, p.FkProducto }).ToListAsync();
            foreach (var g in asoc.GroupBy(a => a.FkRegla))
                porRegla[g.Key] = g.Select(a => a.FkProducto).ToHashSet();
        }

        var lista = reglas.Select(r => new ReglaPromo
        {
            Id = r.Id, Nombre = r.Nombre, Tipo = r.Tipo, Porcentaje = r.Porcentaje, Lleva = r.Lleva, Paga = r.Paga,
            AgrupaVariantes = r.Tipo == TipoPromo.NxM && r.AgrupaVariantes,
            Desde = r.VigenteDesde, Hasta = r.VigenteHasta, Activa = r.Activa,
            ProductoIds = porRegla.GetValueOrDefault(r.Id) ?? new HashSet<int>()
        }).ToList();

        _cache.Set(KeyReglas, lista, Vigencia);
        return lista;
    }

    /// <summary>Un cambio desde el ABM de esta aplicacion se ve de inmediato (los de Comercial Web, en hasta 60 s).</summary>
    private void InvalidarReglas() => _cache.Remove(KeyReglas);

    // Los productos fraccionados (importe por peso/medida) quedan fuera del motor, igual que en Comercial Web.
    private async Task<HashSet<int>> FraccionadosAsync(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return new HashSet<int>();
        var lista = ids.ToList();
        return (await _db.Productos.AsNoTracking()
            .Where(p => lista.Contains(p.Id) && p.Fraccionado == true)
            .Select(p => p.Id).ToListAsync()).ToHashSet();
    }

    /// <summary>
    /// Asigna a cada linea la ficha de la que es variante, SOLO si hay reglas NxM que acumulan variantes y el modulo de
    /// variantes esta habilitado (ecommerce/HabilitarVariantesProducto = 1). En cualquier otro caso no se consulta ninguna
    /// tabla de variantes y el calculo es el de siempre. Una consulta, solo para los productos que estan en esas reglas.
    /// </summary>
    private async Task AsignarFichasAsync(List<LineaPromoEntrada> lineas, List<ReglaPromo> reglas)
    {
        var agrupadas = reglas.Where(r => r.AgrupaVariantes && r.Tipo == TipoPromo.NxM).ToList();
        if (agrupadas.Count == 0 || !await _parametros.HabilitarVariantesProductoAsync()) return;

        var enReglas = agrupadas.SelectMany(r => r.ProductoIds).ToHashSet();
        var ids = lineas.Select(l => l.ProductoId).Distinct().Where(enReglas.Contains).ToList();
        if (ids.Count == 0) return;

        var fichas = await _db.ProductosVariantes.AsNoTracking()
            .Where(v => ids.Contains(v.FkProducto))
            .Select(v => new { v.FkProducto, v.FkPrincipal })
            .ToDictionaryAsync(v => v.FkProducto, v => v.FkPrincipal);
        foreach (var l in lineas)
            if (fichas.TryGetValue(l.ProductoId, out var ficha)) l.FichaId = ficha;
    }

    private static string Cant(decimal v) => v.ToString("0.####", Ar);

    private static string TextoPromo(LineaPromoResultado r, decimal ahorro) =>
        r.Tipo == TipoPromo.NxM
            ? $"Promo {r.Lleva}x{r.Paga}{(r.AgrupaVariantes ? " acumulando variantes" : "")}: {Cant(r.CantBonificada)} unidad{(r.CantBonificada == 1m ? "" : "es")} bonificada{(r.CantBonificada == 1m ? "" : "s")}"
            : $"{(r.Porcentaje ?? 0m).ToString("0.##", Ar)} % de descuento";

    // ═══════════════════════════════════════════════════════════════════════
    // Catalogo y detalle
    // ═══════════════════════════════════════════════════════════════════════
    public async Task AplicarACatalogoAsync(IReadOnlyList<ProductoDto> productos)
    {
        if (productos.Count == 0 || !await HabilitadoAsync()) return;

        var ahora    = HoraArgentina.Ahora();
        var vigentes = (await ReglasAsync()).Where(r => r.EstaVigente(ahora)).OrderBy(r => r.Id).ToList();
        if (vigentes.Count == 0) return;

        // Fichas con variantes: cada variante es un producto con su propia promocion. La ficha no cambia de precio: solo lleva
        // la etiqueta que resume las promociones de sus variantes. Una sola consulta para todas las fichas de la pagina.
        var fichas = productos.Where(p => p.TieneVariantes && !p.Fraccionado).Select(p => p.Id).ToList();
        Dictionary<int, List<int>> variantesDe = new();
        if (fichas.Count > 0)
        {
            var conPromo = vigentes.SelectMany(r => r.ProductoIds).ToHashSet();
            var variantes = await _db.ProductosVariantes.AsNoTracking()
                .Where(v => fichas.Contains(v.FkPrincipal) && v.Activa)
                .Select(v => new { v.FkPrincipal, v.FkProducto }).ToListAsync();
            variantesDe = variantes.GroupBy(v => v.FkPrincipal).ToDictionary(g => g.Key, g => g.Select(v => v.FkProducto).ToList());
            // (solo se conservan las fichas que tienen alguna variante con promocion)
            variantesDe = variantesDe.Where(kv => kv.Value.Any(conPromo.Contains)).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        foreach (var p in productos)
        {
            if (p.TieneVariantes)
            {
                if (variantesDe.TryGetValue(p.Id, out var ids)) EtiquetarFicha(p, ids, vigentes, ahora);
                continue;
            }
            if (p.Fraccionado || p.Precio <= 0m) continue;

            var (pct, nxm) = PromoDe(p.Id, p.Precio, vigentes, ahora);
            if (pct.HasValue)
            {
                p.PromoPorcentaje = pct;
                p.PrecioPromo     = Math.Round(p.Precio * (1m - pct.Value / 100m), 2);
            }
            if (nxm != null) { p.PromoLleva = nxm.Lleva; p.PromoPaga = nxm.Paga; p.PromoAgrupa = nxm.AgrupaVariantes; }
            Etiquetar(p, pct.HasValue ? pct : null, nxm, parcial: false, ficha: false);
        }
    }

    /// <summary>
    /// Promocion vigente de UN producto, con la misma regla de siempre: porcentaje = el motor con cantidad 1 (un porcentaje aplica
    /// siempre; una NxM necesita llevar N); cantidad = la NxM vigente que mas unidades regala por cada una que lleva (a igualdad,
    /// la mas antigua).
    /// </summary>
    private static (decimal? Porcentaje, ReglaPromo? Nxm) PromoDe(int productoId, decimal precio, List<ReglaPromo> vigentes, DateTime ahora)
    {
        var res = MotorPromociones.Evaluar(
            new[] { new LineaPromoEntrada { Clave = 0, ProductoId = productoId, Cantidad = 1m, PrecioSinIva = precio } },
            vigentes, ahora);
        var porc = res.FirstOrDefault(r => r.Tipo == TipoPromo.Porcentaje)?.Porcentaje;

        var nxm = vigentes
            .Where(r => r.Tipo == TipoPromo.NxM && r.Lleva >= 2 && r.Paga >= 1 && r.Paga < r.Lleva && r.ProductoIds.Contains(productoId))
            .OrderByDescending(r => (decimal)(r.Lleva!.Value - r.Paga!.Value) / r.Lleva.Value).ThenBy(r => r.Id)
            .FirstOrDefault();
        return (porc, nxm);
    }

    /// <summary>
    /// Etiqueta de una ficha a partir de las promociones de sus variantes. Si todas las variantes tienen la misma promocion se
    /// muestra tal cual; si solo algunas la tienen, o tienen porcentajes distintos, se aclara ("Hasta", "en algunas opciones").
    /// </summary>
    private static void EtiquetarFicha(ProductoDto ficha, List<int> variantes, List<ReglaPromo> vigentes, DateTime ahora)
    {
        var porcentajes = new List<decimal>();
        var nxms = new List<ReglaPromo>();
        foreach (var id in variantes)
        {
            var (pct, nxm) = PromoDe(id, 1m, vigentes, ahora);     // el precio no influye en que regla corresponde
            if (pct.HasValue) porcentajes.Add(pct.Value);
            if (nxm != null) nxms.Add(nxm);
        }
        if (porcentajes.Count == 0 && nxms.Count == 0) return;
        ficha.PromoGenerica = true;

        decimal? pctFicha = porcentajes.Count > 0 ? porcentajes.Max() : null;
        var mejorNxm = nxms
            .OrderByDescending(r => (decimal)(r.Lleva!.Value - r.Paga!.Value) / r.Lleva.Value).ThenBy(r => r.Id).FirstOrDefault();

        // Parcial = no todas las variantes tienen esa misma promocion
        bool parcialPct = porcentajes.Count > 0 && (porcentajes.Count < variantes.Count || porcentajes.Distinct().Count() > 1);
        bool parcialNxm = mejorNxm != null && (nxms.Count < variantes.Count || nxms.Select(r => r.Id).Distinct().Count() > 1);
        Etiquetar(ficha, pctFicha, mejorNxm, parcial: pctFicha.HasValue ? parcialPct : parcialNxm, ficha: true, parcialNxm: parcialNxm);
    }

    /// <summary>
    /// Texto de la etiqueta (corto, para el distintivo de la tarjeta) y de la condicion (una linea bajo el precio). Un porcentaje
    /// se presenta como descuento directo; una promocion por cantidad siempre con sus condiciones, nunca como descuento incondicional.
    /// </summary>
    private static void Etiquetar(ProductoDto p, decimal? pct, ReglaPromo? nxm, bool parcial, bool ficha, bool? parcialNxm = null)
    {
        string? NxmTexto() => nxm == null ? null : $"Llevá {nxm.Lleva}, pagá {nxm.Paga}";
        string NxmCondicion()
        {
            var n = nxm!;
            if (n.AgrupaVariantes && ficha) return $"Sumá {n.Lleva} unidades combinando variantes";
            if (n.AgrupaVariantes) return $"Sumá {n.Lleva} unidades; podés combinar variantes";
            return ficha ? $"Al llevar {n.Lleva} unidades de la misma variante" : $"Al llevar {n.Lleva} unidades del mismo producto";
        }

        if (pct.HasValue)
        {
            var v = pct.Value.ToString("0.##", Ar);
            p.PromoEtiqueta = parcial ? $"Hasta {v}% OFF" : $"{v}% OFF";
            if (nxm != null) p.PromoCondicion = $"Además: {NxmTexto()!.ToLowerInvariant()}" + (parcialNxm == true ? " (algunas opciones)" : "");
            else if (parcial) p.PromoCondicion = "Según la opción elegida";
        }
        else if (nxm != null)
        {
            p.PromoEtiqueta  = NxmTexto();
            p.PromoCondicion = NxmCondicion() + (parcial ? " · algunas opciones" : "");
        }
    }

    public async Task<PromoDestinoDto> ObtenerParaCatalogoAsync(int reglaId)
    {
        var dto = new PromoDestinoDto { Id = reglaId };
        if (!await HabilitadoAsync()) { dto.Estado = EstadoPromoDestino.Deshabilitada; return dto; }

        var r = await _db.PromocionesReglas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == reglaId);
        if (r is null) { dto.Estado = EstadoPromoDestino.NoExiste; return dto; }

        dto.Nombre = r.Nombre; dto.Tipo = r.Tipo; dto.Desde = r.VigenteDesde; dto.Hasta = r.VigenteHasta;
        var agrupa = r.Tipo == TipoPromo.NxM && r.AgrupaVariantes;
        dto.Beneficio = r.Tipo == TipoPromo.NxM
            ? $"Llevá {r.Lleva}, pagá {r.Paga}"
            : $"{(r.Porcentaje ?? 0m).ToString("0.##", Ar)} % de descuento";
        if (r.Tipo == TipoPromo.NxM)
            dto.Condicion = agrupa
                ? $"Sumá {r.Lleva} unidades combinando las variantes del mismo producto"
                : $"Al llevar {r.Lleva} unidades del mismo producto (o de la misma variante)";

        var ahora = HoraArgentina.Ahora();
        dto.Estado = !r.Activa            ? EstadoPromoDestino.Inactiva
                   : r.VigenteHasta < ahora ? EstadoPromoDestino.Vencida
                   : r.VigenteDesde > ahora ? EstadoPromoDestino.Futura
                   : EstadoPromoDestino.Vigente;
        if (dto.Estado != EstadoPromoDestino.Vigente) return dto;     // fuera de vigencia: no se listan productos

        dto.ProductoIds = await _db.PromocionesReglasProductos.AsNoTracking()
            .Where(p => p.FkRegla == reglaId).Select(p => p.FkProducto).ToListAsync();
        return dto;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Carrito
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<PromoCarritoResultado> EvaluarAsync(
        IEnumerable<(int ProductoId, decimal Cantidad, decimal PrecioBase)> lineas)
    {
        var resp = new PromoCarritoResultado();
        if (!await HabilitadoAsync()) return resp;

        var validas = lineas.Where(l => l.ProductoId > 0 && l.Cantidad > 0 && l.PrecioBase > 0).ToList();
        if (validas.Count == 0) return resp;

        var fracc = await FraccionadosAsync(validas.Select(l => l.ProductoId).Distinct().ToList());
        var entrada = validas.Where(l => !fracc.Contains(l.ProductoId))
            .Select((l, i) => new LineaPromoEntrada { Clave = i, ProductoId = l.ProductoId, Cantidad = l.Cantidad, PrecioSinIva = l.PrecioBase })
            .ToList();
        if (entrada.Count == 0) return resp;

        var ahora  = HoraArgentina.Ahora();
        var reglas = await ReglasAsync();
        await AsignarFichasAsync(entrada, reglas);

        foreach (var r in MotorPromociones.Evaluar(entrada, reglas, ahora).GroupBy(x => x.ProductoId))
        {
            var primero = r.First();
            var ahorro  = r.Sum(x => x.Ahorro);
            resp.PorProducto[r.Key] = new PromoLineaCarritoDto
            {
                ProductoId = r.Key, ReglaId = primero.ReglaId, Nombre = primero.Nombre, Tipo = primero.Tipo,
                Porcentaje = primero.Porcentaje, Lleva = primero.Lleva, Paga = primero.Paga,
                CantBonificada = r.Sum(x => x.CantBonificada), Ahorro = ahorro,
                Texto = TextoPromo(new LineaPromoResultado
                {
                    Tipo = primero.Tipo, Porcentaje = primero.Porcentaje, Lleva = primero.Lleva, Paga = primero.Paga,
                    AgrupaVariantes = primero.AgrupaVariantes, CantBonificada = r.Sum(x => x.CantBonificada)
                }, ahorro)
            };
        }

        foreach (var p in MotorPromociones.Proximas(entrada, reglas, ahora))
            resp.Proximas[p.ProductoId] = new PromoProximaCarritoDto
            {
                ProductoId = p.ProductoId,
                Texto = p.Agrupa
                    ? $"¡Sumá 1 unidad más de cualquier variante de esta ficha incluida en la promoción y llevás {p.Lleva} pagando {p.Paga}! ({p.Nombre})"
                    : $"¡Sumá 1 unidad más y llevás {p.Lleva} pagando {p.Paga}! ({p.Nombre})"
            };
        return resp;
    }

    /// <summary>
    /// Evalua las lineas del pedido borrador (entidades seguidas por el contexto) y devuelve, por indice de linea, la
    /// promocion que corresponde. Cada linea se evalua con su precio BASE (precioOrig), nunca con el ya descontado.
    /// </summary>
    private async Task<(List<PedidoDetalle> Lineas, Dictionary<int, LineaPromoResultado> Res)> CalcularAsync(int pedidoId)
    {
        var lineas = await _db.PedidoDetalles.Where(d => d.FkPedido == pedidoId).OrderBy(d => d.Linea).ToListAsync();
        var res = new Dictionary<int, LineaPromoResultado>();
        if (lineas.Count == 0) return (lineas, res);

        var prodIds = lineas.Where(l => l.FkProducto.HasValue).Select(l => l.FkProducto!.Value).Distinct().ToList();
        var fracc   = await FraccionadosAsync(prodIds);

        var entrada = new List<LineaPromoEntrada>();
        for (var i = 0; i < lineas.Count; i++)
        {
            var l = lineas[i];
            if (!l.FkProducto.HasValue || fracc.Contains(l.FkProducto.Value)) continue;
            entrada.Add(new LineaPromoEntrada
            {
                Clave = i, ProductoId = l.FkProducto.Value, Cantidad = l.Cantidad ?? 0m, PrecioSinIva = Base(l)
            });
        }

        if (entrada.Count > 0)
        {
            var reglas = await ReglasAsync();
            await AsignarFichasAsync(entrada, reglas);
            foreach (var r in MotorPromociones.Evaluar(entrada, reglas, HoraArgentina.Ahora()))
                res[r.Clave] = r;
        }
        return (lineas, res);
    }

    private static decimal Base(PedidoDetalle l) => l.PrecioOrig ?? l.PrecioSinIva ?? l.PrecioConIva ?? 0m;

    /// <summary>Deja la linea con su precio normal (sin promocion).</summary>
    private static void Restablecer(PedidoDetalle d)
    {
        var precio = Base(d);
        var cant   = d.Cantidad ?? 0m;
        d.PrecioConIva   = precio;
        d.Subtotal       = precio * cant;
        d.SubtotalSinIva = d.Subtotal;
        if ((d.Descuento ?? 0m) > 0m) d.Descuento = null;
    }

    /// <summary>Aplica la promocion a la linea (forma "carrito": una linea por producto).</summary>
    private static void Aplicar(PedidoDetalle d, LineaPromoResultado r)
    {
        var precio = Base(d);
        var cant   = d.Cantidad ?? 0m;
        if (r.Tipo == TipoPromo.Porcentaje)
        {
            // Mismo calculo que Comercial Web: precio con descuento por unidad; subtotal = cantidad x ese precio.
            var conIva = Math.Round(precio * (1m - r.DescuentoPct / 100m), 4);
            d.Descuento      = r.DescuentoPct;
            d.PrecioConIva   = conIva;
            d.Subtotal       = cant * conIva;
        }
        else
        {
            // NxM: el precio unitario no cambia; se cobran (cantidad - bonificadas) unidades.
            d.Descuento      = Math.Round(r.DescuentoPct, 2);
            d.PrecioConIva   = precio;
            d.Subtotal       = (cant - r.CantBonificada) * precio;
        }
        d.SubtotalSinIva = d.Subtotal;
    }

    public async Task AplicarAlCarritoAsync(int pedidoId)
    {
        if (!await HabilitadoAsync()) return;

        var estado = await _db.Pedidos.Where(p => p.Id == pedidoId).Select(p => p.EstadoEcommerce).FirstOrDefaultAsync();
        if (estado != "borrador") return;          // un pedido ya confirmado (o en pago) no se vuelve a calcular

        var (lineas, res) = await CalcularAsync(pedidoId);
        for (var i = 0; i < lineas.Count; i++)
        {
            Restablecer(lineas[i]);
            if (res.TryGetValue(i, out var r)) Aplicar(lineas[i], r);
        }
        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Confirmacion: fija las promociones en el pedido
    // ═══════════════════════════════════════════════════════════════════════
    public async Task MaterializarPedidoAsync(int pedidoId)
    {
        if (!await HabilitadoAsync()) return;

        var (lineas, res) = await CalcularAsync(pedidoId);
        if (lineas.Count == 0) return;

        var auditorias = new List<(PedidoDetalle Linea, LineaPromoResultado R, bool Cobrada)>();
        var nuevas     = new List<PedidoDetalle>();

        for (var i = 0; i < lineas.Count; i++)
        {
            var d = lineas[i];
            Restablecer(d);
            if (!res.TryGetValue(i, out var r)) continue;

            if (r.Tipo == TipoPromo.Porcentaje)
            {
                Aplicar(d, r);
                // Convencion de Comercial Web: subtotalSinIva = precio unitario sin IVA ya descontado.
                d.SubtotalSinIva = Base(d) * (1m - r.DescuentoPct / 100m);
                auditorias.Add((d, r, false));
            }
            else
            {
                // NxM: una linea cobrada (precio normal) + una bonificada al 100 %, como Comercial Web.
                var precio = Base(d);
                var total  = d.Cantidad ?? 0m;
                var bonif  = r.CantBonificada;

                // Con acumulacion de variantes una linea puede quedar bonificada ENTERA (ej. 3x2 con 1 unidad de cada variante: la
                // primera sale gratis): no hay parte cobrada, la misma linea pasa a ser la bonificada (como Comercial Web).
                if (bonif >= total)
                {
                    d.Descuento      = 100m;
                    d.PrecioConIva   = 0m;
                    d.Subtotal       = 0m;
                    d.SubtotalSinIva = 0m;
                    auditorias.Add((d, r, false));
                    continue;
                }

                var bonificada = new PedidoDetalle
                {
                    FkPedido = d.FkPedido, FkProducto = d.FkProducto, CodBarras = d.CodBarras, CodProveedor = d.CodProveedor,
                    Descripcion = d.Descripcion, PrecioSinIva = d.PrecioSinIva, Cantidad = bonif,
                    Procesado = false, CantEntregada = 0m, PrecioOrig = d.PrecioOrig, Costo = d.Costo,
                    PrecioConIva = 0m, FkColor = d.FkColor, Observ = d.Observ,
                    Descuento = 100m, Recargo = null, Subtotal = 0m, SubtotalSinIva = 0m
                };

                d.Cantidad       = total - bonif;
                d.Descuento      = null;
                d.PrecioConIva   = precio;
                d.Subtotal       = d.Cantidad * precio;
                d.SubtotalSinIva = precio;

                nuevas.Add(bonificada);
                auditorias.Add((d, r, true));
                auditorias.Add((bonificada, r, false));
            }
        }

        _db.PedidoDetalles.AddRange(nuevas);
        await _db.SaveChangesAsync();            // las lineas nuevas ya tienen su id

        foreach (var (linea, r, cobrada) in auditorias)
        {
            _db.PedidosPromoDetalle.Add(new PedidoPromoDetalle
            {
                FkPedidoDetalle = linea.Linea, FkRegla = r.ReglaId, Nombre = r.Nombre, Tipo = r.Tipo,
                Porcentaje = r.Tipo == TipoPromo.Porcentaje ? r.Porcentaje : null,
                Lleva = r.Lleva, Paga = r.Paga,
                CantBonificada = r.Tipo == TipoPromo.NxM ? r.CantBonificada : null,
                Ahorro = cobrada ? 0m : r.Ahorro
            });
        }

        var pedido = await _db.Pedidos.FindAsync(pedidoId);
        if (pedido is not null)
            pedido.Total = lineas.Concat(nuevas).Sum(l => l.Subtotal ?? 0m);
        await _db.SaveChangesAsync();
    }

    public async Task CopiarAuditoriaAVentaAsync(IReadOnlyList<(long PedidoLinea, long VentaLinea)> pares)
    {
        if (pares.Count == 0 || !await HabilitadoAsync()) return;

        var ids = pares.Select(p => p.PedidoLinea).ToList();
        var auditorias = await _db.PedidosPromoDetalle.AsNoTracking()
            .Where(a => ids.Contains(a.FkPedidoDetalle)).ToListAsync();
        if (auditorias.Count == 0) return;

        var ventaPorPedido = pares.ToDictionary(p => p.PedidoLinea, p => p.VentaLinea);
        foreach (var a in auditorias)
            _db.VentasPromoDetalle.Add(new VentaPromoDetalle
            {
                FkVentaDetalle = ventaPorPedido[a.FkPedidoDetalle], FkRegla = a.FkRegla, Nombre = a.Nombre, Tipo = a.Tipo,
                Porcentaje = a.Porcentaje, Lleva = a.Lleva, Paga = a.Paga, CantBonificada = a.CantBonificada, Ahorro = a.Ahorro
            });
        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ABM (mismas validaciones que Comercial Web)
    // ═══════════════════════════════════════════════════════════════════════
    private static string Resumen(string tipo, decimal? pct, int? lleva, int? paga)
        => tipo == TipoPromo.NxM ? $"{lleva}x{paga}" : $"{(pct ?? 0m).ToString("0.##", Ar)} % de descuento";

    public async Task<List<PromoReglaListDto>> ListarAsync()
    {
        if (!await TablasListasAsync()) return new List<PromoReglaListDto>();

        var reglas = await _db.PromocionesReglas.AsNoTracking()
            .OrderByDescending(r => r.VigenteHasta).ThenBy(r => r.Nombre).ToListAsync();
        var cuentas = await _db.PromocionesReglasProductos.AsNoTracking()
            .GroupBy(p => p.FkRegla)
            .Select(g => new { Regla = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Regla, x => x.Total);

        var ahora = HoraArgentina.Ahora();
        return reglas.Select(r => new PromoReglaListDto
        {
            Id = r.Id, Nombre = r.Nombre, Tipo = r.Tipo,
            Resumen = Resumen(r.Tipo, r.Porcentaje, r.Lleva, r.Paga) + (r.Tipo == TipoPromo.NxM && r.AgrupaVariantes ? " · acumula variantes" : ""),
            AgrupaVariantes = r.Tipo == TipoPromo.NxM && r.AgrupaVariantes,
            Desde = r.VigenteDesde, Hasta = r.VigenteHasta, Activa = r.Activa,
            Vigente = r.Activa && r.VigenteDesde <= ahora && r.VigenteHasta >= ahora,
            CantProductos = cuentas.GetValueOrDefault(r.Id, 0)
        }).ToList();
    }

    public async Task<PromoReglaDetalleDto?> ObtenerAsync(int id)
    {
        if (!await TablasListasAsync()) return null;
        var r = await _db.PromocionesReglas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return null;

        var ids = await _db.PromocionesReglasProductos.AsNoTracking()
            .Where(p => p.FkRegla == id).Select(p => p.FkProducto).ToListAsync();

        return new PromoReglaDetalleDto
        {
            Id = r.Id, Nombre = r.Nombre, Tipo = r.Tipo, Porcentaje = r.Porcentaje, Lleva = r.Lleva, Paga = r.Paga,
            AgrupaVariantes = r.Tipo == TipoPromo.NxM && r.AgrupaVariantes,
            Desde = r.VigenteDesde, Hasta = r.VigenteHasta, Activa = r.Activa, ProductoIds = ids,
            Productos = await ProductosConPrecioAsync(ids)
        };
    }

    public async Task<int> GuardarAsync(PromoReglaDetalleDto dto, string? usuario)
    {
        if (!await TablasListasAsync())
            throw new InvalidOperationException("Faltan crear las tablas de promociones (script scripts/promociones_reglas.sql de Comercial Web).");

        var nombre = (dto.Nombre ?? "").Trim().ToUpperInvariant();
        if (nombre.Length == 0)  throw new InvalidOperationException("Ingrese el nombre de la promoción.");
        if (nombre.Length > 100) throw new InvalidOperationException("El nombre no puede superar los 100 caracteres.");

        var tipo = (dto.Tipo ?? "").Trim().ToUpperInvariant();
        decimal? pct = null; int? lleva = null, paga = null;
        if (tipo == TipoPromo.Porcentaje)
        {
            if (!dto.Porcentaje.HasValue || dto.Porcentaje.Value <= 0m || dto.Porcentaje.Value > 100m)
                throw new InvalidOperationException("El porcentaje de descuento debe ser mayor que 0 y como máximo 100.");
            if (decimal.Round(dto.Porcentaje.Value, 2) != dto.Porcentaje.Value)
                throw new InvalidOperationException("El porcentaje admite como máximo 2 decimales.");
            pct = dto.Porcentaje.Value;
        }
        else if (tipo == TipoPromo.NxM)
        {
            if (!dto.Lleva.HasValue || !dto.Paga.HasValue)
                throw new InvalidOperationException("Indique cuántas unidades lleva y cuántas paga el cliente.");
            if (dto.Lleva.Value < 2 || dto.Lleva.Value > 99)
                throw new InvalidOperationException("La cantidad que lleva el cliente debe estar entre 2 y 99.");
            if (dto.Paga.Value < 1 || dto.Paga.Value >= dto.Lleva.Value)
                throw new InvalidOperationException("La cantidad que paga debe ser al menos 1 y menor que la que lleva.");
            lleva = dto.Lleva; paga = dto.Paga;
        }
        else throw new InvalidOperationException("Tipo de promoción inválido.");

        if (dto.Hasta < dto.Desde)
            throw new InvalidOperationException("La fecha de finalización no puede ser anterior a la de inicio.");

        var prodIds = dto.ProductoIds.Where(i => i > 0).Distinct().ToList();
        if (prodIds.Count == 0) throw new InvalidOperationException("Seleccione al menos un producto.");
        var existentes = await _db.Productos.AsNoTracking().Where(p => prodIds.Contains(p.Id)).CountAsync();
        if (existentes != prodIds.Count) throw new InvalidOperationException("Hay productos seleccionados que ya no existen.");

        // Una ficha de producto con variantes no se vende: la promocion va sobre sus variantes.
        // (las tablas de variantes solo se consultan con ecommerce/HabilitarVariantesProducto = 1)
        if (await _parametros.HabilitarVariantesProductoAsync() && await _db.ProductosPrincipales.AnyAsync(f => prodIds.Contains(f.FkProducto)))
            throw new InvalidOperationException("Hay fichas de productos con variantes: la promoción se asigna a cada variante, no a la ficha.");

        // "Acumular variantes de una misma ficha": solo NxM, y solo si el modulo de variantes esta habilitado. Una regla que ya la
        // tenia (cargada desde Comercial Web) puede conservarla aunque se apague el modulo; activarla de nuevo no.
        var agrupa = tipo == TipoPromo.NxM && dto.AgrupaVariantes;
        if (agrupa && !await _parametros.HabilitarVariantesProductoAsync())
        {
            var yaLaTenia = dto.Id > 0 && await _db.PromocionesReglas.AsNoTracking().AnyAsync(r => r.Id == dto.Id && r.AgrupaVariantes);
            if (!yaLaTenia)
                throw new InvalidOperationException("No se puede acumular variantes: los productos con variantes están deshabilitados (ecommerce / HabilitarVariantesProducto).");
        }

        // Segundos fuera: la vigencia se maneja al minuto
        static DateTime Min(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            PromocionRegla regla;
            if (dto.Id > 0)
                regla = await _db.PromocionesReglas.FirstOrDefaultAsync(r => r.Id == dto.Id)
                        ?? throw new InvalidOperationException("La promoción no existe.");
            else
            {
                regla = new PromocionRegla { FechaAlta = HoraArgentina.Ahora() };
                _db.PromocionesReglas.Add(regla);
            }
            regla.Nombre = nombre; regla.Tipo = tipo; regla.Porcentaje = pct; regla.Lleva = lleva; regla.Paga = paga;
            regla.AgrupaVariantes = agrupa;   // solo existe para NxM; en cualquier otro tipo queda en 0
            regla.VigenteDesde = Min(dto.Desde); regla.VigenteHasta = Min(dto.Hasta);
            regla.Activa = dto.Activa; regla.Usuario = usuario;
            await _db.SaveChangesAsync();

            var viejos = await _db.PromocionesReglasProductos.Where(p => p.FkRegla == regla.Id).ToListAsync();
            _db.PromocionesReglasProductos.RemoveRange(viejos);
            await _db.SaveChangesAsync();
            _db.PromocionesReglasProductos.AddRange(prodIds.Select(id => new PromocionReglaProducto { FkRegla = regla.Id, FkProducto = id }));
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            InvalidarReglas();
            return regla.Id;
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    public async Task EliminarAsync(int id)
    {
        if (!await TablasListasAsync()) return;
        var regla = await _db.PromocionesReglas.FirstOrDefaultAsync(r => r.Id == id);
        if (regla == null) return;
        _db.PromocionesReglasProductos.RemoveRange(_db.PromocionesReglasProductos.Where(p => p.FkRegla == id));
        _db.PromocionesReglas.Remove(regla);
        await _db.SaveChangesAsync();
        InvalidarReglas();
    }

    public async Task<bool> ToggleActivaAsync(int id)
    {
        var regla = await _db.PromocionesReglas.FirstOrDefaultAsync(r => r.Id == id)
                    ?? throw new InvalidOperationException("La promoción no existe.");
        regla.Activa = !regla.Activa;
        await _db.SaveChangesAsync();
        InvalidarReglas();
        return regla.Activa;
    }

    // ── Busqueda de productos para agregar a una promocion ──
    // Proveedor, rubro (con subrubros), texto sobre cod. barras / cod. proveedor / descripcion. Productos dados de baja
    // y fichas de variantes quedan afuera (las variantes si se pueden elegir).
    public async Task<PromoBusquedaProductosDto> BuscarProductosAsync(
        IEnumerable<int>? proveedores, IEnumerable<int>? rubros, string? texto, int max)
    {
        var provs = proveedores?.Where(i => i > 0).ToList();
        var rubs  = rubros?.Where(i => i > 0).ToList();
        var q = _db.Productos.AsNoTracking().Where(p => p.Baja != true);

        if (await _parametros.HabilitarVariantesProductoAsync())
        {
            var fichas = _db.ProductosPrincipales.Select(f => f.FkProducto);
            q = q.Where(p => !fichas.Contains(p.Id));
        }

        if (provs is { Count: > 0 }) q = q.Where(p => p.FkProveedor != null && provs.Contains(p.FkProveedor.Value));
        if (rubs is { Count: > 0 })
        {
            // Un rubro incluye a sus subrubros (el arbol de rubros se lee una sola vez).
            var arbol = (await _db.Rubros.AsNoTracking().Where(r => r.Descripcion != null)
                    .Select(r => new { r.Id, Descripcion = r.Descripcion!, r.FkRubroPadre }).ToListAsync())
                .Select(r => (r.Id, r.Descripcion, r.FkRubroPadre));
            var nodos = RubroArbolHelper.Construir(arbol);
            var ids = rubs.SelectMany(r => RubroArbolHelper.IdsConDescendientes(nodos, r)).Distinct().ToList();
            q = q.Where(p => p.FkRubro != null && ids.Contains(p.FkRubro.Value));
        }
        var t = texto?.Trim();
        if (!string.IsNullOrEmpty(t))
            q = q.Where(p => (p.CodBarras != null && p.CodBarras.Contains(t)) ||
                             (p.CodProveedor != null && p.CodProveedor.Contains(t)) ||
                             (p.Descripcion != null && p.Descripcion.Contains(t)));

        var total  = await q.CountAsync();
        var pagina = await q.OrderBy(p => p.Descripcion).Take(Math.Max(1, max))
            .Select(p => new { p.Id }).ToListAsync();

        return new PromoBusquedaProductosDto
        {
            Total = total,
            Items = await ProductosConPrecioAsync(pagina.Select(x => x.Id).ToList())
        };
    }

    private async Task<List<PromoReglaProductoDto>> ProductosConPrecioAsync(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return new List<PromoReglaProductoDto>();
        var lista = ids.ToList();

        var filas = await (
            from p in _db.Productos.AsNoTracking()
            where lista.Contains(p.Id)
            join pv in _db.Proveedores on p.FkProveedor equals (int?)pv.Id into pvG from pv in pvG.DefaultIfEmpty()
            join rb in _db.Rubros      on p.FkRubro     equals (int?)rb.Id into rbG from rb in rbG.DefaultIfEmpty()
            orderby p.Descripcion
            select new
            {
                p.Id, p.CodProveedor, p.CodBarras, p.Descripcion,
                Proveedor = pv != null ? pv.NombreComercial : null,
                Rubro     = rb != null ? rb.Descripcion     : null,
                Precio    = p.Precio != null ? (p.Precio.Precio ?? 0m) : 0m
            }).ToListAsync();

        return filas.Select(f => new PromoReglaProductoDto
        {
            ProductoId = f.Id, CodProveedor = f.CodProveedor, CodBarras = f.CodBarras, Descripcion = f.Descripcion ?? "",
            Proveedor = f.Proveedor, Rubro = f.Rubro, PrecioActual = f.Precio
        }).ToList();
    }
}
