using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Services;

/// <summary>
/// Réplica de GeneradorCodigoBarrasService de Comercial Web. El contador vive en la tabla parametros
/// (productos/proximoCodigoBarras) y su fila se bloquea con SELECT … FOR UPDATE durante el alta: las altas simultáneas de
/// cualquiera de las dos aplicaciones se serializan y el bloqueo se libera al confirmar o revertir (si el alta falla, el
/// contador también vuelve atrás). No protege contra códigos escritos a mano al mismo tiempo desde otra pantalla (la base
/// no tiene índice único porque ya existen códigos repetidos). Cambios en estas reglas: hacerlos también en Comercial Web.
/// </summary>
public class GeneradorCodigoBarrasService : IGeneradorCodigoBarrasService
{
    private const string Modulo           = "productos";
    private const string ParamHabilitado  = "generaCodigoBarras";
    private const string ParamContador    = "proximoCodigoBarras";
    private const int    TamanoBloque     = 50;          // candidatos verificados por consulta
    private const long   MaximoCandidatos = 1_000_000;   // corte de seguridad

    private readonly AppDbContext _db;

    public GeneradorCodigoBarrasService(AppDbContext db) => _db = db;

    public async Task<bool> HabilitadoAsync()
    {
        var valor = await _db.Parametros.AsNoTracking()
            .Where(p => p.Modulo == Modulo && p.ParametroNombre == ParamHabilitado)
            .Select(p => p.Valor)
            .FirstOrDefaultAsync();
        return valor?.Trim() == "1";
    }

    public async Task<List<string>> ReservarAsync(int cantidad, IEnumerable<string>? excluir = null)
    {
        var codigos = new List<string>();
        if (cantidad <= 0) return codigos;

        if (!HayTransaccion)
            throw new InvalidOperationException("La generación de códigos de barras requiere una transacción activa.");

        // Primera lectura de la transacción: bloquea el contador (las demás altas esperan acá).
        var valor = await LeerContadorBloqueandoAsync()
            ?? throw new InvalidOperationException(
                "La generación automática de códigos está activa pero falta el parámetro productos/proximoCodigoBarras. " +
                "Ejecute el script codigo_barras_automatico.sql (de Comercial Web) en la base de datos.");

        long numero = long.TryParse(valor.Trim(), out var n) && n >= 1 ? n : 1;
        var reservados = new HashSet<string>(
            (excluir ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()),
            StringComparer.OrdinalIgnoreCase);

        long revisados = 0;
        while (codigos.Count < cantidad)
        {
            var bloque = Enumerable.Range(0, TamanoBloque).Select(i => Formatear(numero + i)).ToList();

            // Ocupado = existe como código de barras O de proveedor en cualquier producto (incluidos los de baja).
            var usados = await _db.Productos.AsNoTracking()
                .Where(p => (p.CodBarras != null && bloque.Contains(p.CodBarras)) ||
                            (p.CodProveedor != null && bloque.Contains(p.CodProveedor)))
                .Select(p => new { p.CodBarras, p.CodProveedor })
                .ToListAsync();
            var ocupados = new HashSet<string>(
                usados.SelectMany(u => new[] { u.CodBarras, u.CodProveedor })
                      .Where(c => c != null).Select(c => c!.TrimEnd()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var candidato in bloque)
            {
                numero++;
                revisados++;
                if (ocupados.Contains(candidato) || reservados.Contains(candidato)) continue;
                codigos.Add(candidato);
                if (codigos.Count == cantidad) break;
            }

            if (revisados > MaximoCandidatos)
                throw new InvalidOperationException("No se encontraron códigos de barras libres.");
        }

        await GuardarContadorAsync(numero.ToString());
        return codigos;
    }

    /// <summary>001 … 999, 1000, 1001 … (mínimo 3 dígitos).</summary>
    public static string Formatear(long numero) => numero.ToString("D3");

    // ── Acceso al contador (virtual: las pruebas con SQLite no soportan SELECT … FOR UPDATE) ──
    protected virtual bool HayTransaccion => _db.Database.CurrentTransaction != null;

    protected virtual async Task<string?> LeerContadorBloqueandoAsync()
        => (await _db.Database.SqlQueryRaw<string>(
                "SELECT COALESCE(valor, '1') AS `Value` FROM parametros " +
                "WHERE modulo = 'productos' AND parametro = 'proximoCodigoBarras' FOR UPDATE")
            .ToListAsync()).FirstOrDefault();

    protected virtual async Task GuardarContadorAsync(string valor)
        => await _db.Database.ExecuteSqlRawAsync(
            "UPDATE parametros SET valor = {0} WHERE modulo = {1} AND parametro = {2}", valor, Modulo, ParamContador);
}
