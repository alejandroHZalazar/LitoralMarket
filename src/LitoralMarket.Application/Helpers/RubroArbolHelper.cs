using LitoralMarket.Application.DTOs;

namespace LitoralMarket.Application.Helpers;

/// <summary>
/// Operaciones sobre el árbol de rubros (Rubros.fk_rubroPadre) hechas en memoria sobre
/// la lista cacheada, para no consultar la base en cada página.
/// </summary>
public static class RubroArbolHelper
{
    /// <summary>
    /// Arma la lista ordenada en profundidad (padre, sus hijos, ...) con ruta y nivel.
    /// Un rubro cuyo padre no existe, o que forma un ciclo, se trata como principal.
    /// </summary>
    public static List<RubroNodoDto> Construir(IEnumerable<(int Id, string Descripcion, int? PadreId)> rubros)
    {
        var todos = rubros.ToList();
        var ids   = todos.Select(r => r.Id).ToHashSet();

        var hijosDe = todos
            .Where(r => r.PadreId.HasValue && ids.Contains(r.PadreId.Value) && r.PadreId.Value != r.Id)
            .GroupBy(r => r.PadreId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Descripcion, StringComparer.CurrentCultureIgnoreCase).ToList());

        var resultado = new List<RubroNodoDto>(todos.Count);
        var visitados = new HashSet<int>();

        void Agregar((int Id, string Descripcion, int? PadreId) r, string rutaPadre, int nivel, int? padre)
        {
            if (!visitados.Add(r.Id)) return;
            var ruta = rutaPadre.Length == 0 ? r.Descripcion : $"{rutaPadre} > {r.Descripcion}";
            resultado.Add(new RubroNodoDto { Id = r.Id, Descripcion = r.Descripcion, PadreId = padre, Ruta = ruta, Nivel = nivel });
            if (hijosDe.TryGetValue(r.Id, out var hijos))
                foreach (var h in hijos) Agregar(h, ruta, nivel + 1, r.Id);
        }

        var raices = todos
            .Where(r => !r.PadreId.HasValue || !ids.Contains(r.PadreId.Value) || r.PadreId.Value == r.Id)
            .OrderBy(r => r.Descripcion, StringComparer.CurrentCultureIgnoreCase);
        foreach (var r in raices) Agregar(r, string.Empty, 1, null);

        // Rubros atrapados en un ciclo (no alcanzables desde ninguna raíz): se muestran igual.
        foreach (var r in todos.Where(r => !visitados.Contains(r.Id)))
            Agregar(r, string.Empty, 1, null);

        return resultado;
    }

    /// <summary>El rubro indicado más todos sus descendientes (cualquier nivel).</summary>
    public static List<int> IdsConDescendientes(IReadOnlyList<RubroNodoDto> arbol, int rubroId)
    {
        var hijosDe = arbol.Where(n => n.PadreId.HasValue)
                           .ToLookup(n => n.PadreId!.Value, n => n.Id);
        var resultado = new List<int> { rubroId };
        var pendientes = new Queue<int>();
        pendientes.Enqueue(rubroId);
        while (pendientes.Count > 0)
            foreach (var h in hijosDe[pendientes.Dequeue()])
                if (!resultado.Contains(h)) { resultado.Add(h); pendientes.Enqueue(h); }
        return resultado;
    }

    /// <summary>Cadena de la raíz hasta el rubro indicado (inclusive), para migas de pan.</summary>
    public static List<RubroNodoDto> Cadena(IReadOnlyList<RubroNodoDto> arbol, int rubroId)
    {
        var porId = arbol.ToDictionary(n => n.Id);
        var cadena = new List<RubroNodoDto>();
        var actual = rubroId;
        while (porId.TryGetValue(actual, out var nodo) && cadena.All(c => c.Id != nodo.Id))
        {
            cadena.Insert(0, nodo);
            if (!nodo.PadreId.HasValue) break;
            actual = nodo.PadreId.Value;
        }
        return cadena;
    }

    /// <summary>
    /// Deja solo los rubros que tienen productos asignados directamente o que tienen
    /// algún descendiente con productos (un padre sin productos propios se conserva para
    /// poder navegar a sus subrubros). Mantiene el orden en profundidad de la lista.
    /// </summary>
    public static List<RubroNodoDto> ConProductos(IReadOnlyList<RubroNodoDto> arbol, ISet<int> idsConProductos)
    {
        var porId    = arbol.ToDictionary(n => n.Id);
        var visibles = new HashSet<int>();

        foreach (var id in idsConProductos)
        {
            // Marca el rubro y todos sus ancestros (corta si ya estaba marcado o hay un ciclo).
            var actual = id;
            while (porId.TryGetValue(actual, out var nodo) && visibles.Add(nodo.Id) && nodo.PadreId.HasValue)
                actual = nodo.PadreId.Value;
        }

        return arbol.Where(n => visibles.Contains(n.Id)).ToList();
    }

    /// <summary>La raíz del árbol a la que pertenece el rubro (o él mismo si es principal).</summary>
    public static int? RaizDe(IReadOnlyList<RubroNodoDto> arbol, int rubroId) =>
        Cadena(arbol, rubroId).FirstOrDefault()?.Id;
}
