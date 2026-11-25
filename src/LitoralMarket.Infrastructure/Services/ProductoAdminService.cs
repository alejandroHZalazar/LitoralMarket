using LitoralMarket.Application.DTOs;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Domain.Entities;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LitoralMarket.Infrastructure.Services;

public class ProductoAdminService : IProductoAdminService
{
    private readonly AppDbContext _db;
    private readonly IProductoRepository _repo;
    private readonly IParametrosService _params;
    private readonly IGeneradorCodigoBarrasService _codigos;

    public ProductoAdminService(AppDbContext db, IProductoRepository repo, IParametrosService parametros,
                                IGeneradorCodigoBarrasService codigos)
    {
        _codigos = codigos;
        _db     = db;
        _repo   = repo;
        _params = parametros;
    }

    // Ruta completa del rubro ("Padre > Subrubro") tomada del árbol cacheado: sin consultas extra.
    private static string? RutaDe(IReadOnlyDictionary<int, string> rutas, int? fkRubro) =>
        fkRubro.HasValue && rutas.TryGetValue(fkRubro.Value, out var ruta) ? ruta : null;

    private async Task<Dictionary<int, string>> RutasRubrosAsync() =>
        (await _repo.ObtenerArbolRubrosAsync()).ToDictionary(n => n.Id, n => n.Ruta);

    // ──────────────────────────────────────────────────────────────
    // Búsqueda
    // ──────────────────────────────────────────────────────────────
    public async Task<List<ProductoAdminDto>> BuscarAsync(int tipo, string valor,
        ContextoBusquedaProducto contexto = ContextoBusquedaProducto.Todos)
    {
        // Con variantes apagadas (valor cacheado) la búsqueda es exactamente la de siempre.
        var variantes = contexto != ContextoBusquedaProducto.Todos && await _params.HabilitarVariantesProductoAsync();

        var q = _db.Productos.Include(p => p.Rubro).AsQueryable();

        q = tipo switch
        {
            0 => q.Where(p => p.CodProveedor != null && p.CodProveedor.Contains(valor)),
            1 => q.Where(p => p.CodBarras    != null && p.CodBarras.Contains(valor)),
            _ => q.Where(p => p.Descripcion  != null && p.Descripcion.Contains(valor))
        };
        q = q.Where(p => p.Baja != true);

        // Administración: si el texto coincide con una VARIANTE (ej. su código de barras), se muestra su ficha.
        List<int> fichasDeCoincidencias = new();
        if (variantes && contexto == ContextoBusquedaProducto.Administracion)
            fichasDeCoincidencias = await (from v in _db.ProductosVariantes
                                           join m in q on v.FkProducto equals m.Id
                                           select v.FkPrincipal).Distinct().ToListAsync();

        if (variantes)
        {
            if (contexto == ContextoBusquedaProducto.ParaStock)
                q = q.Where(p => !_db.ProductosPrincipales.Any(pp => pp.FkProducto == p.Id));      // la ficha no tiene stock propio
            else
                q = q.Where(p => !_db.ProductosVariantes.Any(v => v.FkProducto == p.Id));          // las variantes se ven por su ficha
        }

        // Proyección directa: NO materializa la entidad Producto (evita cargar el
        // blob 'imagen' de hasta 200 productos, que antes se traía sin usarse).
        var lista = await q
            .OrderBy(p => p.Descripcion)
            .Take(200)
            .Select(p => new ProductoAdminDto
            {
                Id           = p.Id,
                CodProveedor = p.CodProveedor,
                CodBarras    = p.CodBarras,
                Descripcion  = p.Descripcion,
                FkRubro      = p.FkRubro,
                RubroNombre  = p.Rubro != null ? p.Rubro.Descripcion : null
            })
            .ToListAsync();

        if (fichasDeCoincidencias.Count > 0)
        {
            var yaEstan = lista.Select(x => x.Id).ToHashSet();
            var faltan = fichasDeCoincidencias.Where(i => !yaEstan.Contains(i)).ToList();
            if (faltan.Count > 0)
            {
                var extra = await _db.Productos
                    .Where(p => faltan.Contains(p.Id) && p.Baja != true)
                    .Select(p => new ProductoAdminDto
                    {
                        Id = p.Id, CodProveedor = p.CodProveedor, CodBarras = p.CodBarras, Descripcion = p.Descripcion,
                        FkRubro = p.FkRubro, RubroNombre = p.Rubro != null ? p.Rubro.Descripcion : null
                    })
                    .ToListAsync();
                lista = lista.Concat(extra).OrderBy(x => x.Descripcion).Take(200).ToList();
            }
        }

        var rutas = await RutasRubrosAsync();
        foreach (var dto in lista)
            dto.RubroRuta = RutaDe(rutas, dto.FkRubro);

        // Marca fichas (con su cantidad de variantes) y variantes (con su ficha) para mostrarlas en la grilla.
        if (variantes && lista.Count > 0)
        {
            var ids = lista.Select(x => x.Id).ToList();
            var fichas = await _db.ProductosPrincipales.Where(pp => ids.Contains(pp.FkProducto))
                .Select(pp => pp.FkProducto).ToListAsync();
            var cantidades = await _db.ProductosVariantes.Where(v => ids.Contains(v.FkPrincipal))
                .GroupBy(v => v.FkPrincipal).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync();
            var padres = await _db.ProductosVariantes.Where(v => ids.Contains(v.FkProducto))
                .Select(v => new { v.FkProducto, v.FkPrincipal }).ToListAsync();

            foreach (var dto in lista)
            {
                dto.EsFicha       = fichas.Contains(dto.Id);
                dto.CantVariantes = cantidades.FirstOrDefault(c => c.Id == dto.Id)?.N ?? 0;
                dto.FkPrincipal   = padres.FirstOrDefault(pd => pd.FkProducto == dto.Id)?.FkPrincipal;
            }
        }

        return lista;
    }

    // ──────────────────────────────────────────────────────────────
    // Detalle completo
    // ──────────────────────────────────────────────────────────────
    public async Task<ProductoAdminDto?> ObtenerPorIdAsync(int id)
    {
        var p = await _db.Productos
            .Include(x => x.Rubro)
            .Include(x => x.Stock)
            .Include(x => x.Precio)
            .Include(x => x.Costo)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p is null) return null;

        string? proveedorNombre = null;
        if (p.FkProveedor.HasValue)
            proveedorNombre = await _db.Proveedores
                .Where(pr => pr.Id == p.FkProveedor.Value)
                .Select(pr => pr.NombreComercial)
                .FirstOrDefaultAsync();

        return new ProductoAdminDto
        {
            Id               = p.Id,
            CodProveedor     = p.CodProveedor,
            CodBarras        = p.CodBarras,
            Descripcion      = p.Descripcion,
            DescripcionLarga = p.DescripcionLarga,
            FkRubro          = p.FkRubro,
            RubroNombre      = p.Rubro?.Descripcion,
            RubroRuta        = RutaDe(await RutasRubrosAsync(), p.FkRubro),
            FkProveedor      = p.FkProveedor,
            ProveedorNombre  = proveedorNombre,
            Iva              = p.Iva ?? 0,
            PrecioCosto      = p.Costo?.Costo ?? 0,
            PrecioLista      = p.Precio?.Precio ?? 0,
            StockActual      = p.Stock?.Cantidad ?? 0,
            StockMinimo      = p.Stock?.CantidadMinima ?? 0,
            // Columna NOT NULL DEFAULT 1.00 en 'ale'; se resuelve a 1 igual que la BD
            // por si el código llega a correr contra una base donde aún sea nullable.
            CantidadMinimaVenta = p.CantidadMinimaVenta ?? 1,
            EsPromocion      = p.EsPromocion ?? false,
            Fraccionado      = p.Fraccionado ?? false,
            Dolarizado       = p.Dolarizado  ?? false,
            Baja             = p.Baja        ?? false
        };
    }

    // ──────────────────────────────────────────────────────────────
    // Imágenes (tabla imagenesProductos)
    // ──────────────────────────────────────────────────────────────
    public async Task<List<ProductoImagenDto>> ObtenerImagenesAsync(int productoId) =>
        await _db.ImagenesProductos
            .Where(i => i.FkProducto == productoId && !i.Baja)
            .OrderByDescending(i => i.EsPrincipal).ThenBy(i => i.Orden).ThenBy(i => i.Id)
            .Select(i => new ProductoImagenDto { Id = i.Id, EsPrincipal = i.EsPrincipal, Orden = i.Orden })
            .ToListAsync();

    public async Task SincronizarImagenesAsync(int productoId, List<ImagenSyncItem> imagenes)
    {
        imagenes ??= new List<ImagenSyncItem>();

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var actuales = await _db.ImagenesProductos
                .Where(i => i.FkProducto == productoId && !i.Baja)
                .ToListAsync();

            var idsRecibidos = imagenes.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();

            // Baja lógica de las que ya no están en la lista
            foreach (var img in actuales.Where(a => !idsRecibidos.Contains(a.Id)))
                img.Baja = true;

            // Conservar existentes (orden/principal) e insertar nuevas
            foreach (var item in imagenes)
            {
                ImagenProducto? entidad = null;

                if (item.Id.HasValue)
                {
                    entidad = actuales.FirstOrDefault(a => a.Id == item.Id.Value);
                    if (entidad is null) continue; // id que no pertenece → se ignora
                }
                else if (!string.IsNullOrWhiteSpace(item.DataUrl) && item.DataUrl.Contains(','))
                {
                    var coma = item.DataUrl.IndexOf(',');
                    var meta = item.DataUrl[..coma];                 // data:image/png;base64
                    var b64  = item.DataUrl[(coma + 1)..];
                    var ct   = meta.StartsWith("data:") ? meta[5..].Split(';')[0] : null;

                    entidad = new ImagenProducto
                    {
                        FkProducto  = productoId,
                        Imagen      = Convert.FromBase64String(b64),
                        ContentType = ct,
                        Baja        = false,
                        FechaAlta   = DateTime.Now
                    };
                    _db.ImagenesProductos.Add(entidad);
                }

                if (entidad is null) continue;
                entidad.Orden       = item.Orden;
                entidad.EsPrincipal = item.EsPrincipal;
            }

            await _db.SaveChangesAsync();

            // Garantizar exactamente una principal entre las activas
            var activas = await _db.ImagenesProductos
                .Where(i => i.FkProducto == productoId && !i.Baja)
                .OrderBy(i => i.Orden).ThenBy(i => i.Id)
                .ToListAsync();

            if (activas.Count > 0)
            {
                var ppal = activas.FirstOrDefault(a => a.EsPrincipal) ?? activas[0];
                foreach (var a in activas)
                    a.EsPrincipal = a.Id == ppal.Id;
                await _db.SaveChangesAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Alta
    // ──────────────────────────────────────────────────────────────
    public async Task<int> CrearAsync(ProductoAdminDto dto, bool generarCodigo = true)
    {
        // Código automático (productos/generaCodigoBarras = '1'): solo si no se cargó ninguno de los dos. El parámetro se
        // consulta antes de abrir la transacción; la reserva es la primera lectura dentro de ella (misma lógica que Comercial Web).
        var generar = generarCodigo
                      && string.IsNullOrWhiteSpace(dto.CodBarras) && string.IsNullOrWhiteSpace(dto.CodProveedor)
                      && await _codigos.HabilitadoAsync();

        // Con generación, alta atómica (si falla, el contador vuelve atrás). Si quien llama ya abrió una transacción, se une a ella.
        await using var tx = generar && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync()
            : null;
        if (generar)
        {
            var codigo = (await _codigos.ReservarAsync(1))[0];
            dto.CodBarras = dto.CodProveedor = codigo;
        }

        var producto = new Producto
        {
            CodProveedor     = dto.CodProveedor,
            CodBarras        = dto.CodBarras,
            Descripcion      = dto.Descripcion,
            DescripcionLarga = dto.DescripcionLarga,
            FkRubro          = dto.FkRubro,
            FkProveedor      = dto.FkProveedor,
            Iva              = (int?)dto.Iva,
            EsPromocion      = dto.EsPromocion,
            Fraccionado      = dto.Fraccionado,
            Dolarizado       = dto.Dolarizado,
            CantidadMinimaVenta = dto.CantidadMinimaVenta,
            Baja             = false
            // Imagen legacy: ya NO se escribe. Las imágenes van a imagenesProductos.
        };
        _db.Productos.Add(producto);
        await _db.SaveChangesAsync();

        _db.StockProductos.Add(new StockProducto
        {
            FkProducto     = producto.Id,
            Cantidad       = dto.StockActual,
            CantidadMinima = dto.StockMinimo
        });
        _db.PreciosProductos.Add(new PrecioProducto
        {
            FkProducto = producto.Id,
            Precio     = dto.PrecioLista
        });
        _db.CostosProductos.Add(new CostoProducto
        {
            FkProducto = producto.Id,
            Costo      = dto.PrecioCosto
        });
        await _db.SaveChangesAsync();
        if (tx is not null) await tx.CommitAsync();   // sin confirmar, al descartarse la transacción se revierte todo

        return producto.Id;
    }

    // ──────────────────────────────────────────────────────────────
    // Modificación
    // ──────────────────────────────────────────────────────────────
    public async Task ActualizarAsync(ProductoAdminDto dto)
    {
        // No se hace Include(Producto.Imagenes) → no se cargan blobs al editar.
        var p = await _db.Productos
            .Include(x => x.Stock)
            .Include(x => x.Precio)
            .Include(x => x.Costo)
            .FirstOrDefaultAsync(x => x.Id == dto.Id);

        if (p is null) return;

        p.CodProveedor     = dto.CodProveedor;
        p.CodBarras        = dto.CodBarras;
        p.Descripcion      = dto.Descripcion;
        p.DescripcionLarga = dto.DescripcionLarga;
        p.FkRubro          = dto.FkRubro;
        p.FkProveedor      = dto.FkProveedor;
        p.Iva              = (int?)dto.Iva;
        p.EsPromocion      = dto.EsPromocion;
        p.Fraccionado      = dto.Fraccionado;
        p.Dolarizado       = dto.Dolarizado;
        p.CantidadMinimaVenta = dto.CantidadMinimaVenta;
        // Imagen legacy: ya NO se escribe. Las imágenes se gestionan en imagenesProductos.

        // Stock
        if (p.Stock is null)
            _db.StockProductos.Add(new StockProducto { FkProducto = p.Id, Cantidad = dto.StockActual, CantidadMinima = dto.StockMinimo });
        else
        {
            p.Stock.Cantidad       = dto.StockActual;
            p.Stock.CantidadMinima = dto.StockMinimo;
        }

        // Precio lista
        if (p.Precio is null)
            _db.PreciosProductos.Add(new PrecioProducto { FkProducto = p.Id, Precio = dto.PrecioLista });
        else
            p.Precio.Precio = dto.PrecioLista;

        // Costo
        if (p.Costo is null)
            _db.CostosProductos.Add(new CostoProducto { FkProducto = p.Id, Costo = dto.PrecioCosto });
        else
            p.Costo.Costo = dto.PrecioCosto;

        await _db.SaveChangesAsync();
    }

    // ──────────────────────────────────────────────────────────────
    // Baja lógica
    // ──────────────────────────────────────────────────────────────
    public async Task BajaLogicaAsync(int id)
    {
        var p = await _db.Productos.FindAsync(id);
        if (p is null) return;
        p.Baja = true;

        // Con variantes habilitadas: dar de baja una ficha da de baja todas sus variantes, y dar de baja una
        // variante deshabilita su combinación. Con el parámetro apagado esto no consulta nada.
        if (await _params.HabilitarVariantesProductoAsync())
        {
            var variante = await _db.ProductosVariantes.FirstOrDefaultAsync(v => v.FkProducto == id);
            if (variante is not null) variante.Activa = false;

            var hijas = await _db.ProductosVariantes.Where(v => v.FkPrincipal == id)
                .Select(v => v.FkProducto).ToListAsync();
            if (hijas.Count > 0)
                await _db.Productos.Where(x => hijas.Contains(x.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Baja, true));
        }

        await _db.SaveChangesAsync();
    }

    // ──────────────────────────────────────────────────────────────
    // Ingreso masivo de stock (transaccional)
    // ──────────────────────────────────────────────────────────────
    public async Task IngresoStockMasivoAsync(IEnumerable<IngresoItemRequest> items)
    {
        // Agrupar por productoId por si el caller envía duplicados
        var agrupados = items
            .GroupBy(i => i.ProductoId)
            .Select(g => new
            {
                ProductoId  = g.Key,
                Cantidad    = g.Sum(i => i.Cantidad),
                Observacion = g.First().Observacion
            })
            .ToList();

        if (!agrupados.Any())
            throw new InvalidOperationException("No se recibieron productos para ingresar.");

        if (agrupados.Any(a => a.Cantidad <= 0))
            throw new InvalidOperationException("Todas las cantidades deben ser mayores a cero.");

        // Cargar todos los productos de una sola consulta
        var ids      = agrupados.Select(a => a.ProductoId).ToList();
        var productos = await _db.Productos
            .Include(p => p.Stock)
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();

        var faltantes = ids.Except(productos.Select(p => p.Id)).ToList();
        if (faltantes.Any())
            throw new InvalidOperationException(
                $"Productos no encontrados: {string.Join(", ", faltantes)}.");

        // Ejecutar todo en una transacción
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in agrupados)
            {
                var producto = productos.First(p => p.Id == item.ProductoId);
                var stockAnt = producto.Stock?.Cantidad ?? 0m;
                var stockAct = stockAnt + item.Cantidad;

                if (producto.Stock is null)
                    _db.StockProductos.Add(new StockProducto
                    {
                        FkProducto = item.ProductoId,
                        Cantidad   = stockAct
                    });
                else
                    producto.Stock.Cantidad = stockAct;

                _db.ProductosMovimientos.Add(new ProductosMovimiento
                {
                    FkProducto     = item.ProductoId,
                    TipoMovimiento = 1,
                    Descripcion    = string.IsNullOrWhiteSpace(item.Observacion)
                                         ? (producto.Descripcion ?? "Ingreso de stock")
                                         : item.Observacion,
                    StockAnt       = stockAnt,
                    StockAct       = stockAct,
                    Cantidad       = item.Cantidad,
                    Costo          = 0,
                    Venta          = 0,
                    FechaMov       = DateTime.Now
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Ingreso de stock (producto individual)
    // ──────────────────────────────────────────────────────────────
    public async Task<decimal> IngresoStockAsync(int productoId, decimal cantidad, string? observacion)
    {
        if (cantidad <= 0)
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad debe ser mayor a cero.");

        var producto = await _db.Productos
            .Include(p => p.Stock)
            .FirstOrDefaultAsync(p => p.Id == productoId);

        if (producto is null)
            throw new InvalidOperationException("Producto no encontrado.");

        var stockAnt = producto.Stock?.Cantidad ?? 0m;
        var stockAct = stockAnt + cantidad;

        // Crear el registro de stock si no existe
        if (producto.Stock is null)
        {
            _db.StockProductos.Add(new StockProducto
            {
                FkProducto = productoId,
                Cantidad   = stockAct
            });
        }
        else
        {
            producto.Stock.Cantidad = stockAct;
        }

        // Movimiento de ingreso (TipoMovimiento = 1)
        _db.ProductosMovimientos.Add(new ProductosMovimiento
        {
            FkProducto     = productoId,
            TipoMovimiento = 1,
            Descripcion    = string.IsNullOrWhiteSpace(observacion)
                                 ? (producto.Descripcion ?? "Ingreso de stock")
                                 : observacion,
            StockAnt       = stockAnt,
            StockAct       = stockAct,
            Cantidad       = cantidad,
            Costo          = 0,
            Venta          = 0,
            FechaMov       = DateTime.Now
        });

        await _db.SaveChangesAsync();
        return stockAct;
    }
}
