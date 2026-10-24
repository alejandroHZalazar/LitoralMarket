using System.Data;
using LitoralMarket.Application.Interfaces;
using LitoralMarket.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LitoralMarket.Infrastructure.Services;

public class RubroAdminService : IRubroAdminService
{
    // Tipos de operación de sp_RubrosABM
    private const int Alta = 1, Modificacion = 2, Baja = 3;

    private readonly AppDbContext _db;
    private readonly IProductoRepository _productos;

    public RubroAdminService(AppDbContext db, IProductoRepository productos)
    {
        _db        = db;
        _productos = productos;
    }

    public async Task<List<RubroAdminDto>> BuscarAsync(string valor)
    {
        var arbol = await _productos.ObtenerArbolRubrosAsync();

        var cantidades = await _db.Productos
            .Where(p => p.Baja != true && p.FkRubro != null)
            .GroupBy(p => p.FkRubro!.Value)
            .Select(g => new { Id = g.Key, Cant = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Cant);

        var q = arbol.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(valor))
            q = q.Where(n => n.Ruta.Contains(valor.Trim(), StringComparison.CurrentCultureIgnoreCase));

        return q.Select(n => new RubroAdminDto
        {
            Id            = n.Id,
            Descripcion   = n.Descripcion,
            PadreId       = n.PadreId,
            Ruta          = n.Ruta,
            Nivel         = n.Nivel,
            CantProductos = cantidades.GetValueOrDefault(n.Id)
        }).ToList();
    }

    public async Task<(bool Ok, string? Error)> GuardarAsync(int id, string descripcion, int? padreId)
    {
        var codigo = await LlamarSpAsync(id, id == 0 ? Alta : Modificacion, descripcion.Trim(), padreId);
        return Resultado(codigo);
    }

    public async Task<(bool Ok, string? Error)> EliminarAsync(int id) =>
        Resultado(await LlamarSpAsync(id, Baja, null, null));

    // ── sp_RubrosABM ────────────────────────────────────────────────
    // Valida (descripción, padre, duplicado, ciclos, subrubros, productos), aplica el cambio
    // y reconstruye RubrosArbol. Devuelve el id del rubro, o un código negativo si falló.
    private async Task<int> LlamarSpAsync(int id, int tipo, string? descripcion, int? padreId)
    {
        var conn  = _db.Database.GetDbConnection();
        var abrir = conn.State != ConnectionState.Open;
        if (abrir) await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "sp_RubrosABM";
            cmd.CommandType = CommandType.StoredProcedure;

            void Param(string nombre, DbType tipoDb, object? valor, ParameterDirection dir = ParameterDirection.Input)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = nombre;
                p.DbType        = tipoDb;
                p.Direction     = dir;
                p.Value         = valor ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }

            Param("unId",           DbType.Int32,  id);
            Param("unTipo",         DbType.Int32,  tipo);
            Param("unaDescripcion", DbType.String, descripcion);
            Param("unPadre",        DbType.Int32,  padreId);
            Param("salida",         DbType.Int32,  null, ParameterDirection.Output);

            await cmd.ExecuteNonQueryAsync();
            var salida = Convert.ToInt32(cmd.Parameters["salida"].Value);

            // El árbol cambió: descartar el cache que usan el menú y el catálogo.
            if (salida > 0) _productos.InvalidarCacheRubros();
            return salida;
        }
        finally
        {
            if (abrir) await conn.CloseAsync();
        }
    }

    private static (bool Ok, string? Error) Resultado(int codigo) => codigo switch
    {
        > 0 => (true, null),
        -2  => (false, "Un rubro no puede ser hijo de sí mismo ni de uno de sus subrubros."),
        -3  => (false, "El rubro tiene subrubros: eliminá o mové primero sus subrubros."),
        -4  => (false, "El rubro tiene productos asignados (incluidos los dados de baja) y no puede eliminarse."),
        -5  => (false, "Ya existe un rubro con ese nombre dentro del mismo rubro padre."),
        -6  => (false, "El rubro padre seleccionado no existe."),
        -7  => (false, "La descripción es obligatoria."),
        _   => (false, "No se pudo guardar el cambio en el rubro (error en la base de datos).")
    };
}
