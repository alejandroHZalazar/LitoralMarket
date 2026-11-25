using System.Text.RegularExpressions;
using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Productos con variantes (medidas y/o colores). Cada variante ES una fila de <c>Productos</c> con su propio
/// stock, precio y costo, enlazada a una ficha principal (<c>productosPrincipales</c>/<c>productosVariantes</c>).
/// Para el carrito, el pedido, la venta, el stock y el sistema comercial una variante es un producto común.
/// </summary>
public class VarianteAdminService : IVarianteAdminService
{
    private static readonly Regex HexValido = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private const int MaxNombreAtributo = 40;
    private const int MaxDescripcion    = 150;   // Productos.descripcion es VARCHAR(150) en 'ale'
    private const int MaxCodigo         = 45;    // Productos.codBarras / codProveedor son VARCHAR(45)

    private readonly AppDbContext                 _db;
    private readonly IProductoAdminService        _productos;
    private readonly IParametrosService           _params;
    private readonly ILogger<VarianteAdminService> _logger;
    private readonly IGeneradorCodigoBarrasService _codigos;

    public VarianteAdminService(AppDbContext db, IProductoAdminService productos, IParametrosService parametros,
        ILogger<VarianteAdminService> logger, IGeneradorCodigoBarrasService codigos)
    {
        _codigos   = codigos;
        _db        = db;
        _productos = productos;
        _params    = parametros;
        _logger    = logger;
    }

    // ═════════════════════════════════════════════════════════════
    // Tablas
    // ═════════════════════════════════════════════════════════════
    public async Task<bool> TablasListasAsync()
    {
        try
        {
            await _db.VariantesMedidas.AsNoTracking().Select(x => x.Id).FirstOrDefaultAsync();
            await _db.VariantesColores.AsNoTracking().Select(x => x.Id).FirstOrDefaultAsync();
            await _db.ProductosPrincipales.AsNoTracking().Select(x => x.FkProducto).FirstOrDefaultAsync();
            await _db.ProductosVariantes.AsNoTracking().Select(x => x.FkProducto).FirstOrDefaultAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Variantes: faltan tablas — ¿se corrió sql/variantes_01_tablas.sql?");
            return false;
        }
    }

    // ═════════════════════════════════════════════════════════════
    // Maestros: medidas y colores
    // ═════════════════════════════════════════════════════════════
    public async Task<List<AtributoVarianteDto>> ListarMedidasAsync(bool soloActivas = false)
    {
        var q = _db.VariantesMedidas.AsNoTracking().AsQueryable();
        if (soloActivas) q = q.Where(m => m.Activa);
        return await q.OrderBy(m => m.Orden).ThenBy(m => m.Nombre)
            .Select(m => new AtributoVarianteDto
            {
                Id = m.Id, Nombre = m.Nombre, Orden = m.Orden, Activa = m.Activa,
                EnUso = _db.ProductosVariantes.Count(v => v.FkMedida == m.Id)
            })
            .ToListAsync();
    }

    public async Task<List<AtributoVarianteDto>> ListarColoresAsync(bool soloActivas = false)
    {
        var q = _db.VariantesColores.AsNoTracking().AsQueryable();
        if (soloActivas) q = q.Where(c => c.Activa);
        return await q.OrderBy(c => c.Orden).ThenBy(c => c.Nombre)
            .Select(c => new AtributoVarianteDto
            {
                Id = c.Id, Nombre = c.Nombre, CodigoHex = c.CodigoHex, Orden = c.Orden, Activa = c.Activa,
                EnUso = _db.ProductosVariantes.Count(v => v.FkColor == c.Id)
            })
            .ToListAsync();
    }

    public async Task<ResultadoAtributo> GuardarMedidaAsync(int? id, string nombre, int orden, bool activa)
    {
        var error = ValidarNombreAtributo(ref nombre, "medida");
        if (error is not null) return ResultadoAtributo.Fallo(error);

        var lower = nombre.ToLower();
        var propio = id ?? 0;
        if (await _db.VariantesMedidas.AnyAsync(m => m.Id != propio && m.Nombre.ToLower() == lower))
            return ResultadoAtributo.Fallo("Ya existe una medida con ese nombre.");

        VarianteMedida entidad;
        var renombrada = false;
        if (propio > 0)
        {
            entidad = await _db.VariantesMedidas.FirstOrDefaultAsync(m => m.Id == propio)
                      ?? throw new InvalidOperationException("La medida no existe.");
            renombrada = entidad.Nombre != nombre;
        }
        else
        {
            entidad = new VarianteMedida();
            _db.VariantesMedidas.Add(entidad);
        }
        entidad.Nombre = nombre;
        entidad.Orden  = orden;
        entidad.Activa = activa;
        await _db.SaveChangesAsync();

        if (renombrada) await RegenerarDescripcionesDeAtributoAsync(medidaId: entidad.Id, colorId: null);
        return ResultadoAtributo.Exito(entidad.Id);
    }

    public async Task<ResultadoAtributo> GuardarColorAsync(int? id, string nombre, string? codigoHex, int orden, bool activa)
    {
        var error = ValidarNombreAtributo(ref nombre, "color");
        if (error is not null) return ResultadoAtributo.Fallo(error);

        codigoHex = string.IsNullOrWhiteSpace(codigoHex) ? null : codigoHex.Trim();
        if (codigoHex is not null && !HexValido.IsMatch(codigoHex))
            return ResultadoAtributo.Fallo("El color debe tener el formato #RRGGBB (por ejemplo #1F2937).");

        var lower = nombre.ToLower();
        var propio = id ?? 0;
        if (await _db.VariantesColores.AnyAsync(c => c.Id != propio && c.Nombre.ToLower() == lower))
            return ResultadoAtributo.Fallo("Ya existe un color con ese nombre.");

        VarianteColor entidad;
        var renombrado = false;
        if (propio > 0)
        {
            entidad = await _db.VariantesColores.FirstOrDefaultAsync(c => c.Id == propio)
                      ?? throw new InvalidOperationException("El color no existe.");
            renombrado = entidad.Nombre != nombre;
        }
        else
        {
            entidad = new VarianteColor();
            _db.VariantesColores.Add(entidad);
        }
        entidad.Nombre    = nombre;
        entidad.CodigoHex = codigoHex;
        entidad.Orden     = orden;
        entidad.Activa    = activa;
        await _db.SaveChangesAsync();

        if (renombrado) await RegenerarDescripcionesDeAtributoAsync(medidaId: null, colorId: entidad.Id);
        return ResultadoAtributo.Exito(entidad.Id);
    }

    public async Task<ResultadoAtributo> EliminarMedidaAsync(int id)
    {
        if (await _db.ProductosVariantes.AnyAsync(v => v.FkMedida == id))
            return ResultadoAtributo.Fallo("La medida está en uso por productos con variantes: deshabilitala en lugar de eliminarla.");
        var m = await _db.VariantesMedidas.FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return ResultadoAtributo.Fallo("La medida no existe.");
        _db.VariantesMedidas.Remove(m);
        await _db.SaveChangesAsync();
        return ResultadoAtributo.Exito(id);
    }

    public async Task<ResultadoAtributo> EliminarColorAsync(int id)
    {
        if (await _db.ProductosVariantes.AnyAsync(v => v.FkColor == id))
            return ResultadoAtributo.Fallo("El color está en uso por productos con variantes: deshabilitalo en lugar de eliminarlo.");
        var c = await _db.VariantesColores.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return ResultadoAtributo.Fallo("El color no existe.");
        _db.VariantesColores.Remove(c);
        await _db.SaveChangesAsync();
        return ResultadoAtributo.Exito(id);
    }

    private static string? ValidarNombreAtributo(ref string nombre, string que)
    {
        nombre = (nombre ?? string.Empty).Trim();
        if (nombre.Length == 0) return $"Escribí el nombre de la {que}.";
        if (nombre.Length > MaxNombreAtributo) return $"El nombre admite hasta {MaxNombreAtributo} caracteres.";
        // El nombre se escribe en Productos.descripcion (latin1): sin emojis ni caracteres fuera de ese juego.
        if (!EsLatin1(nombre)) return "El nombre solo admite letras, números y símbolos comunes (sin emojis).";
        return null;
    }

    /// <summary>Renombrar una medida/color regenera la descripción de las variantes que la usan.</summary>
    private async Task RegenerarDescripcionesDeAtributoAsync(int? medidaId, int? colorId)
    {
        var principalIds = await _db.ProductosVariantes
            .Where(v => (medidaId != null && v.FkMedida == medidaId) || (colorId != null && v.FkColor == colorId))
            .Select(v => v.FkPrincipal)
            .Distinct()
            .ToListAsync();
        if (principalIds.Count == 0) return;

        var medidas = await _db.VariantesMedidas.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Nombre);
        var colores = await _db.VariantesColores.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre);

        foreach (var principalId in principalIds)
        {
            var ficha = await _db.ProductosPrincipales.FirstOrDefaultAsync(x => x.FkProducto == principalId);
            var nombreBase = await _db.Productos.Where(x => x.Id == principalId).Select(x => x.Descripcion).FirstOrDefaultAsync();
            if (ficha is null || string.IsNullOrWhiteSpace(nombreBase)) continue;

            var variantes = await _db.ProductosVariantes.Include(v => v.Producto)
                .Where(v => v.FkPrincipal == principalId).ToListAsync();
            foreach (var v in variantes)
            {
                var desc = Generar(nombreBase, ficha.UsaMedidas, ficha.UsaColores,
                    medidas.GetValueOrDefault(v.FkMedida), colores.GetValueOrDefault(v.FkColor));
                if (v.Producto is not null && desc.Length <= MaxDescripcion && EsLatin1(desc))
                    v.Producto.Descripcion = desc;
            }
        }
        await _db.SaveChangesAsync();
    }

    // ═════════════════════════════════════════════════════════════
    // Lectura de una ficha
    // ═════════════════════════════════════════════════════════════
    public async Task<FichaVariantesDto> ObtenerFichaAsync(int productoId)
    {
        var ficha = await _db.ProductosPrincipales.AsNoTracking().FirstOrDefaultAsync(x => x.FkProducto == productoId);
        if (ficha is null) return new FichaVariantesDto { ProductoId = productoId, TieneVariantes = false };

        var comunCosto  = await _db.CostosProductos.Where(c => c.FkProducto == productoId).Select(c => c.Costo).FirstOrDefaultAsync();
        var comunPrecio = await _db.PreciosProductos.Where(c => c.FkProducto == productoId).Select(c => c.Precio).FirstOrDefaultAsync();

        var medidas = await _db.VariantesMedidas.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Nombre);
        var colores = await _db.VariantesColores.AsNoTracking().ToDictionaryAsync(c => c.Id, c => new { c.Nombre, c.CodigoHex });

        var filas = await _db.ProductosVariantes.AsNoTracking()
            .Where(v => v.FkPrincipal == productoId)
            .OrderBy(v => v.Orden).ThenBy(v => v.FkProducto)
            .Select(v => new
            {
                v.FkProducto, v.FkMedida, v.FkColor, v.Activa, v.Orden,
                v.Producto!.CodBarras, v.Producto.CodProveedor, v.Producto.Descripcion,
                Stock  = v.Producto.Stock  != null ? (v.Producto.Stock.Cantidad ?? 0m) : 0m,
                Costo  = v.Producto.Costo  != null ? v.Producto.Costo.Costo  : null,
                Precio = v.Producto.Precio != null ? v.Producto.Precio.Precio : null
            })
            .ToListAsync();

        return new FichaVariantesDto
        {
            ProductoId     = productoId,
            TieneVariantes = true,
            UsaMedidas     = ficha.UsaMedidas,
            UsaColores     = ficha.UsaColores,
            PreciosIguales = ficha.PreciosIguales,
            Variantes = filas.Select(f => new VarianteFilaDto
            {
                ProductoId   = f.FkProducto,
                MedidaId     = f.FkMedida,
                ColorId      = f.FkColor,
                MedidaNombre = medidas.GetValueOrDefault(f.FkMedida),
                ColorNombre  = colores.TryGetValue(f.FkColor, out var col) ? col.Nombre : null,
                ColorHex     = colores.TryGetValue(f.FkColor, out var col2) ? col2.CodigoHex : null,
                Habilitada   = f.Activa,
                CodBarras    = f.CodBarras,
                CodProveedor = f.CodProveedor,
                Costo        = f.Costo,
                Precio       = f.Precio,
                Stock        = f.Stock,
                Orden        = f.Orden,
                Descripcion  = f.Descripcion,
                PrecioPropio = ficha.PreciosIguales && (f.Costo != comunCosto || f.Precio != comunPrecio)
            }).ToList()
        };
    }

    public async Task<VistaPreviaPreciosDto> VistaPreviaPrecioComunAsync(int productoId, decimal nuevoCosto, decimal nuevoPrecio)
    {
        var viejoCosto  = await _db.CostosProductos.Where(c => c.FkProducto == productoId).Select(c => c.Costo).FirstOrDefaultAsync();
        var viejoPrecio = await _db.PreciosProductos.Where(c => c.FkProducto == productoId).Select(c => c.Precio).FirstOrDefaultAsync();

        var filas = await _db.ProductosVariantes.AsNoTracking()
            .Where(v => v.FkPrincipal == productoId)
            .Select(v => new
            {
                v.Producto!.Descripcion,
                Costo  = v.Producto.Costo  != null ? v.Producto.Costo.Costo  : null,
                Precio = v.Producto.Precio != null ? v.Producto.Precio.Precio : null
            })
            .ToListAsync();

        var vista = new VistaPreviaPreciosDto();
        foreach (var f in filas)
        {
            // "Sigue al común" = su costo y su precio actuales son iguales a los comunes ANTERIORES.
            var sigue = viejoCosto.HasValue && viejoPrecio.HasValue && f.Costo == viejoCosto && f.Precio == viejoPrecio;
            if (sigue) vista.Siguen++;
            else
            {
                vista.ConPrecioPropio++;
                if (vista.Propias.Count < 20) vista.Propias.Add(f.Descripcion ?? "(sin descripción)");
            }
        }
        return vista;
    }

    // ═════════════════════════════════════════════════════════════
    // Guardar ficha + variantes
    // ═════════════════════════════════════════════════════════════
    public async Task<ResultadoVariantes> GuardarFichaAsync(ProductoAdminDto p, GuardarVariantesRequest req)
    {
        if (!await _params.HabilitarVariantesProductoAsync())
            return ResultadoVariantes.Fallo(new[] { "Las variantes de producto no están habilitadas en esta base." });
        if (!await TablasListasAsync())
            return ResultadoVariantes.Fallo(new[] { "Faltan las tablas de variantes: ejecutá sql/variantes_01_tablas.sql antes de usarlas." });

        var errores = new List<string>();
        var filas   = req.Variantes ?? new List<VarianteFilaDto>();
        var nombreBase = (p.Descripcion ?? string.Empty).Trim();

        if (nombreBase.Length == 0) errores.Add("Escribí la descripción del producto.");
        if (!req.UsaMedidas && !req.UsaColores) errores.Add("Elegí si el producto varía por medidas, colores o ambos.");
        if (filas.Count == 0) errores.Add("Agregá al menos una variante.");

        // ── Estado actual de la ficha ────────────────────────────────────────
        ProductoPrincipal? principal = null;
        var existentes = new List<ProductoVariante>();
        if (p.Id > 0)
        {
            principal = await _db.ProductosPrincipales.FirstOrDefaultAsync(x => x.FkProducto == p.Id);
            if (principal is null)
                errores.Add("Este producto ya existe como producto simple. Para usar variantes, creá un producto nuevo.");
            else
                existentes = await _db.ProductosVariantes
                    .Include(v => v.Producto).ThenInclude(x => x!.Stock)
                    .Include(v => v.Producto).ThenInclude(x => x!.Precio)
                    .Include(v => v.Producto).ThenInclude(x => x!.Costo)
                    .Where(v => v.FkPrincipal == p.Id)
                    .ToListAsync();
        }

        if (principal is not null && existentes.Count > 0 &&
            (principal.UsaMedidas != req.UsaMedidas || principal.UsaColores != req.UsaColores))
            errores.Add("No se pueden cambiar las dimensiones (medidas/colores) de un producto que ya tiene variantes.");

        // ── Atributos del maestro ────────────────────────────────────────────
        var medidas = await _db.VariantesMedidas.AsNoTracking().ToDictionaryAsync(m => m.Id);
        var colores = await _db.VariantesColores.AsNoTracking().ToDictionaryAsync(c => c.Id);

        var idsFamilia = existentes.Select(v => v.FkProducto).ToHashSet();
        var porId      = existentes.ToDictionary(v => v.FkProducto);
        var combosExistentes = existentes.Select(v => (v.FkMedida, v.FkColor)).ToHashSet();
        var combosFilas      = new HashSet<(int, int)>();
        var descripciones    = new Dictionary<VarianteFilaDto, string>();

        foreach (var f in filas)
        {
            if (!req.UsaMedidas) f.MedidaId = 0;
            else if (f.MedidaId <= 0) { errores.Add("Hay una fila sin medida."); continue; }
            if (!req.UsaColores) f.ColorId = 0;
            else if (f.ColorId <= 0) { errores.Add("Hay una fila sin color."); continue; }

            medidas.TryGetValue(f.MedidaId, out var medida);
            colores.TryGetValue(f.ColorId, out var color);
            var etiqueta = Etiqueta(medida?.Nombre, color?.Nombre);

            if (req.UsaMedidas && medida is null) { errores.Add($"La medida de la fila '{etiqueta}' no existe."); continue; }
            if (req.UsaColores && color  is null) { errores.Add($"El color de la fila '{etiqueta}' no existe."); continue; }

            if (!combosFilas.Add((f.MedidaId, f.ColorId)))
                errores.Add($"La combinación {etiqueta} está repetida.");

            if (f.ProductoId.HasValue)
            {
                if (!porId.TryGetValue(f.ProductoId.Value, out var ex))
                    errores.Add($"La variante {etiqueta} no pertenece a este producto.");
                else if (ex.FkMedida != f.MedidaId || ex.FkColor != f.ColorId)
                    errores.Add($"No se puede cambiar la medida o el color de la variante existente {etiqueta}.");
            }
            else
            {
                if (combosExistentes.Contains((f.MedidaId, f.ColorId)))
                    errores.Add($"La combinación {etiqueta} ya existe en este producto.");
                if (f.Habilitada && medida is { Activa: false }) errores.Add($"La medida {medida.Nombre} está deshabilitada.");
                if (f.Habilitada && color  is { Activa: false }) errores.Add($"El color {color.Nombre} está deshabilitado.");
            }

            // Descripción generada: cabe en Productos.descripcion y es compatible con latin1.
            var desc = Generar(nombreBase, req.UsaMedidas, req.UsaColores, medida?.Nombre, color?.Nombre);
            if (desc.Length > MaxDescripcion)
                errores.Add($"La descripción '{desc}' supera los {MaxDescripcion} caracteres: acortá el nombre del producto, la medida o el color.");
            else if (!EsLatin1(desc))
                errores.Add($"La descripción '{desc}' tiene caracteres que la base no admite (por ejemplo emojis).");
            descripciones[f] = desc;

            // Códigos
            f.CodBarras    = Limpiar(f.CodBarras);
            f.CodProveedor = Limpiar(f.CodProveedor);
            if (f.CodBarras?.Length    > MaxCodigo) errores.Add($"El código de barras de {etiqueta} admite hasta {MaxCodigo} caracteres.");
            if (f.CodProveedor?.Length > MaxCodigo) errores.Add($"El código de proveedor de {etiqueta} admite hasta {MaxCodigo} caracteres.");

            // Stock inicial: entero, solo variantes nuevas
            if (f.StockInicial < 0 || f.StockInicial != decimal.Truncate(f.StockInicial))
                errores.Add($"El stock inicial de {etiqueta} debe ser un número entero mayor o igual a cero.");
            if (f.ProductoId.HasValue) f.StockInicial = 0;

            if ((f.Costo ?? 0) < 0 || (f.Precio ?? 0) < 0)
                errores.Add($"El costo y el precio de {etiqueta} no pueden ser negativos.");
        }

        // ── Códigos repetidos: dentro de la grilla y contra el resto de los productos ─────
        var idsPropios = new HashSet<int>(idsFamilia);
        if (p.Id > 0) idsPropios.Add(p.Id);

        foreach (var g in filas.Where(f => TieneCodigoDeBarras(f.CodBarras))
                               .GroupBy(f => f.CodBarras!.ToLowerInvariant()).Where(g => g.Count() > 1))
            errores.Add($"El código de barras {g.First().CodBarras} está repetido en la grilla.");
        foreach (var g in filas.Where(f => f.CodProveedor is not null)
                               .GroupBy(f => f.CodProveedor!.ToLowerInvariant()).Where(g => g.Count() > 1))
            errores.Add($"El código de proveedor {g.First().CodProveedor} está repetido en la grilla.");

        var listaBarras = filas.Where(f => TieneCodigoDeBarras(f.CodBarras)).Select(f => f.CodBarras!).Distinct().ToList();
        if (listaBarras.Count > 0)
        {
            // Los marcadores vacíos ("", "0", "000") no cuentan: en 'ale' hay decenas repetidos.
            var choques = await _db.Productos.AsNoTracking()
                .Where(x => x.CodBarras != null && listaBarras.Contains(x.CodBarras) && x.Baja != true && !idsPropios.Contains(x.Id))
                .Select(x => new { x.CodBarras, x.Descripcion })
                .Take(10).ToListAsync();
            foreach (var c in choques)
                errores.Add($"El código de barras {c.CodBarras} ya lo usa el producto '{c.Descripcion}'.");
        }

        var listaProv = filas.Where(f => f.CodProveedor is not null).Select(f => f.CodProveedor!).Distinct().ToList();
        if (listaProv.Count > 0)
        {
            var fkProveedor = p.FkProveedor;
            var choques = await _db.Productos.AsNoTracking()
                .Where(x => x.CodProveedor != null && listaProv.Contains(x.CodProveedor) && x.Baja != true
                            && x.FkProveedor == fkProveedor && !idsPropios.Contains(x.Id))
                .Select(x => new { x.CodProveedor, x.Descripcion })
                .Take(10).ToListAsync();
            foreach (var c in choques)
                errores.Add($"El código de proveedor {c.CodProveedor} ya lo usa el producto '{c.Descripcion}' (mismo proveedor).");
        }

        // ── Precios ──────────────────────────────────────────────────────────
        if (req.PreciosIguales)
        {
            if (p.PrecioLista <= 0) errores.Add("Cargá el precio de venta común (mayor a cero).");
            if (p.PrecioCosto < 0)  errores.Add("El costo común no puede ser negativo.");
        }

        // Costo/precio efectivo de cada fila (nueva: propio o común; existente: propio o el actual)
        var efectivos = new Dictionary<VarianteFilaDto, (decimal Costo, decimal Precio)>();
        foreach (var f in filas)
        {
            porId.TryGetValue(f.ProductoId ?? -1, out var ex);
            var costo  = f.Costo  ?? ex?.Producto?.Costo?.Costo   ?? (req.PreciosIguales ? p.PrecioCosto : 0m);
            var precio = f.Precio ?? ex?.Producto?.Precio?.Precio ?? (req.PreciosIguales ? p.PrecioLista : 0m);
            efectivos[f] = (costo, precio);

            if (f.ProductoId is null && f.Habilitada && precio <= 0)
                errores.Add($"Falta el precio de venta de {Etiqueta(f.MedidaNombre ?? medidas.GetValueOrDefault(f.MedidaId)?.Nombre,
                                                                    f.ColorNombre  ?? colores.GetValueOrDefault(f.ColorId)?.Nombre)}.");
        }

        if (errores.Count > 0)
            return ResultadoVariantes.Fallo(errores.Distinct());

        // ── Valores comunes anteriores (para decidir qué variantes "siguen" al cambiar el común) ──
        decimal? viejoCosto = null, viejoPrecio = null;
        var bajaPrincipal = false;
        if (principal is not null)
        {
            viejoCosto  = await _db.CostosProductos.Where(c => c.FkProducto == p.Id).Select(c => c.Costo).FirstOrDefaultAsync();
            viejoPrecio = await _db.PreciosProductos.Where(c => c.FkProducto == p.Id).Select(c => c.Precio).FirstOrDefaultAsync();
            bajaPrincipal = await _db.Productos.Where(x => x.Id == p.Id).Select(x => x.Baja).FirstOrDefaultAsync() == true;
        }

        // Sin precios iguales, la ficha guarda como referencia el menor costo/precio de las variantes habilitadas.
        if (!req.PreciosIguales)
        {
            var habilitadas = filas.Where(f => f.Habilitada).Select(f => efectivos[f]).ToList();
            p.PrecioLista = habilitadas.Count > 0 ? habilitadas.Min(e => e.Precio) : 0m;
            p.PrecioCosto = habilitadas.Count > 0 ? habilitadas.Min(e => e.Costo)  : 0m;
        }
        // El stock es de cada variante: la ficha nunca tiene stock propio.
        p.StockActual = 0;

        var resultado = new ResultadoVariantes();
        var nuevas = new List<(Producto Prod, VarianteFilaDto Fila)>();

        // Código automático (productos/generaCodigoBarras = '1') para las variantes NUEVAS sin ninguno de los dos códigos,
        // también las que se crean deshabilitadas. La ficha principal nunca recibe código. El parámetro se consulta antes de
        // abrir la transacción.
        var sinCodigo = filas.Where(f => f.ProductoId is null && f.CodBarras is null && f.CodProveedor is null).ToList();
        if (sinCodigo.Count > 0 && !await _codigos.HabilitadoAsync()) sinCodigo.Clear();

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Primera lectura de la transacción: se reservan TODOS los códigos de la operación juntos, excluyendo los escritos
            // a mano en la misma operación.
            if (sinCodigo.Count > 0)
            {
                var escritos = filas.SelectMany(f => new[] { f.CodBarras, f.CodProveedor })
                    .Append(p.CodBarras).Append(p.CodProveedor)
                    .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!);
                var reservados = await _codigos.ReservarAsync(sinCodigo.Count, escritos.ToList());
                for (var i = 0; i < sinCodigo.Count; i++)
                    sinCodigo[i].CodBarras = sinCodigo[i].CodProveedor = reservados[i];
                resultado.CodigosAsignados = reservados;
            }

            // 1) Ficha principal (producto + stock/precio/costo) con el servicio existente, en esta misma transacción.
            if (principal is null) p.Id = await _productos.CrearAsync(p, generarCodigo: false);
            else                   await _productos.ActualizarAsync(p);
            resultado.ProductoId = p.Id;

            if (principal is null)
            {
                principal = new ProductoPrincipal { FkProducto = p.Id };
                _db.ProductosPrincipales.Add(principal);
            }
            principal.UsaMedidas     = req.UsaMedidas;
            principal.UsaColores     = req.UsaColores;
            principal.PreciosIguales = req.PreciosIguales;
            await _db.SaveChangesAsync();
            await AsegurarPrecioProveedorAsync(p.Id, p.PrecioCosto);

            // 2) Variantes del pedido
            var enviadas = new HashSet<int>();
            var orden = 0;
            foreach (var f in filas)
            {
                orden++;
                var desc = descripciones[f];
                var (costo, precio) = efectivos[f];

                if (f.ProductoId is null)
                {
                    var prod = new Producto
                    {
                        CodProveedor = f.CodProveedor, CodBarras = f.CodBarras, Descripcion = desc,
                        FkRubro = p.FkRubro, FkProveedor = p.FkProveedor, Iva = (int?)p.Iva,
                        EsPromocion = p.EsPromocion, Fraccionado = p.Fraccionado, Dolarizado = p.Dolarizado,
                        CantidadMinimaVenta = p.CantidadMinimaVenta,
                        Baja   = bajaPrincipal || !f.Habilitada,
                        Stock  = new StockProducto  { Cantidad = 0, CantidadMinima = p.StockMinimo },
                        Precio = new PrecioProducto { Precio = precio },
                        Costo  = new CostoProducto  { Costo  = costo }
                    };
                    _db.Productos.Add(prod);
                    _db.ProductosVariantes.Add(new ProductoVariante
                    {
                        Producto = prod, FkPrincipal = p.Id, FkMedida = f.MedidaId, FkColor = f.ColorId,
                        Activa = f.Habilitada, Orden = f.Orden > 0 ? f.Orden : orden
                    });
                    nuevas.Add((prod, f));
                    resultado.Creadas++;
                    continue;
                }

                var ex   = porId[f.ProductoId.Value];
                var prodEx = ex.Producto!;
                enviadas.Add(ex.FkProducto);

                prodEx.CodBarras    = f.CodBarras;
                prodEx.CodProveedor = f.CodProveedor;
                prodEx.Descripcion  = desc;

                if (f.Habilitada != ex.Activa)
                {
                    ex.Activa = f.Habilitada;
                    if (f.Habilitada) resultado.Habilitadas++; else resultado.Deshabilitadas++;
                }
                if (f.Orden > 0) ex.Orden = f.Orden;

                // Precios de una variante existente: valor propio de la fila, o la regla de propagación del común.
                var costoActual  = prodEx.Costo?.Costo;
                var precioActual = prodEx.Precio?.Precio;
                var nuevoCosto   = costoActual  ?? 0m;
                var nuevoPrecio  = precioActual ?? 0m;
                var propagado    = false;

                if (f.Costo.HasValue) nuevoCosto = f.Costo.Value;
                else if (req.PreciosIguales && DebePropagar(req.ModoPropagacion, viejoCosto, costoActual))
                { nuevoCosto = p.PrecioCosto; propagado |= nuevoCosto != costoActual; }

                if (f.Precio.HasValue) nuevoPrecio = f.Precio.Value;
                else if (req.PreciosIguales && DebePropagar(req.ModoPropagacion, viejoPrecio, precioActual))
                { nuevoPrecio = p.PrecioLista; propagado |= nuevoPrecio != precioActual; }

                if (propagado) resultado.PreciosPropagados++;
                if (req.PreciosIguales && !f.Costo.HasValue && !f.Precio.HasValue
                    && (nuevoCosto != p.PrecioCosto || nuevoPrecio != p.PrecioLista))
                    resultado.PreciosOmitidos++;   // quedó con algún valor propio (distinto del común)

                if (prodEx.Costo  is null) prodEx.Costo  = new CostoProducto  { Costo  = nuevoCosto };  else prodEx.Costo.Costo   = nuevoCosto;
                if (prodEx.Precio is null) prodEx.Precio = new PrecioProducto { Precio = nuevoPrecio }; else prodEx.Precio.Precio = nuevoPrecio;
                resultado.Actualizadas++;   // la baja de cada variante se resuelve abajo, junto con los atributos comunes
            }

            // 3) Atributos comunes y baja en TODAS las variantes existentes (también las que no vinieron en el pedido)
            foreach (var ex in existentes)
            {
                var prodEx = ex.Producto!;
                prodEx.FkRubro             = p.FkRubro;
                prodEx.FkProveedor         = p.FkProveedor;
                prodEx.Iva                 = (int?)p.Iva;
                prodEx.EsPromocion         = p.EsPromocion;
                prodEx.Fraccionado         = p.Fraccionado;
                prodEx.Dolarizado          = p.Dolarizado;
                prodEx.CantidadMinimaVenta = p.CantidadMinimaVenta;
                prodEx.Baja                = bajaPrincipal || !ex.Activa;

                if (!enviadas.Contains(ex.FkProducto))
                {
                    var desc = Generar(nombreBase, req.UsaMedidas, req.UsaColores,
                        medidas.GetValueOrDefault(ex.FkMedida)?.Nombre, colores.GetValueOrDefault(ex.FkColor)?.Nombre);
                    if (desc.Length <= MaxDescripcion && EsLatin1(desc)) prodEx.Descripcion = desc;
                }
            }

            await _db.SaveChangesAsync();

            // 4) Fila de precio de proveedor de cada variante nueva (Comercial Web la necesita para abrir el producto)
            foreach (var (prod, _) in nuevas)
                _db.PreciosProveedores.Add(new PrecioProveedor { FkProducto = prod.Id, Precio = prod.Costo?.Costo ?? 0m });
            if (nuevas.Count > 0) await _db.SaveChangesAsync();

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        // 5) Stock inicial (opcional) con el ingreso de stock existente, que registra su movimiento. Va aparte:
        //    el ingreso abre su propia transacción. Si fallara, las variantes ya quedaron creadas.
        var ingresos = nuevas.Where(n => n.Fila.StockInicial > 0)
            .Select(n => new IngresoItemRequest(n.Prod.Id, (int)n.Fila.StockInicial, "Stock inicial")).ToList();
        if (ingresos.Count > 0)
        {
            try { await _productos.IngresoStockMasivoAsync(ingresos); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Variantes: no se pudo registrar el stock inicial de la ficha #{Id}", resultado.ProductoId);
                resultado.AvisoStockInicial = "Las variantes se crearon, pero no se pudo cargar el stock inicial: cargalo desde Ingreso de stock.";
            }
        }

        resultado.Ok = true;
        return resultado;
    }

    // ═════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// ¿Se le aplica a la variante el nuevo valor común? Modo "todas": sí. Modo "solo quienes siguen": solo si su
    /// valor actual es igual al común anterior. Modo "ninguna": no.
    /// </summary>
    private static bool DebePropagar(int modo, decimal? comunAnterior, decimal? valorActual) => modo switch
    {
        ModoPropagacionPrecio.Todas             => true,
        ModoPropagacionPrecio.SoloQuienesSiguen => comunAnterior.HasValue && valorActual == comunAnterior,
        _                                       => false
    };

    /// <summary>Crea la fila de preciosProveedores del producto si no existe (una por producto).</summary>
    private async Task AsegurarPrecioProveedorAsync(int productoId, decimal precio)
    {
        if (await _db.PreciosProveedores.AnyAsync(x => x.FkProducto == productoId)) return;
        _db.PreciosProveedores.Add(new PrecioProveedor { FkProducto = productoId, Precio = precio });
        await _db.SaveChangesAsync();
    }

    /// <summary>"NOMBRE BASE - MEDIDA - COLOR" (solo las dimensiones que usa la ficha).</summary>
    internal static string Generar(string nombreBase, bool usaMedidas, bool usaColores, string? medida, string? color)
    {
        var partes = new List<string> { nombreBase.Trim() };
        if (usaMedidas && !string.IsNullOrWhiteSpace(medida)) partes.Add(medida.Trim());
        if (usaColores && !string.IsNullOrWhiteSpace(color))  partes.Add(color.Trim());
        return string.Join(" - ", partes);
    }

    private static string Etiqueta(string? medida, string? color) =>
        string.Join(" / ", new[] { medida, color }.Where(s => !string.IsNullOrWhiteSpace(s)).DefaultIfEmpty("(sin atributos)"));

    private static string? Limpiar(string? s)
    {
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    /// <summary>Un código de barras "real": ni vacío ni solo ceros (en 'ale' hay decenas de "0" repetidos).</summary>
    internal static bool TieneCodigoDeBarras(string? codigo) =>
        !string.IsNullOrWhiteSpace(codigo) && codigo.Any(c => c != '0');

    /// <summary>La base de productos es latin1: solo se admiten caracteres hasta U+00FF.</summary>
    internal static bool EsLatin1(string s) => s.All(c => c <= 'ÿ');
}
