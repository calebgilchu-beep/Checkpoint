using System.Globalization;
using ListaJuegos.Core.Connectors;
using ListaJuegos.Core.Suggest;

namespace ListaJuegos.Core.Cli;

/// <summary>Error de una edición que hay que contar a la persona (juego no encontrado, ambiguo, repetido…).</summary>
public sealed class ListEditException(string message) : Exception(message);

/// <summary>
/// Ediciones de la lista sin interfaz (las usa la línea de comandos). Siguen las mismas reglas que la ventana:
/// «Marcar como completado» lleva el juego a Completados, a su misma saga, y le pone la fecha de hoy si no tenía.
/// Solo cambian las líneas tocadas: el resto conserva su texto original (Raw).
/// </summary>
public static class ListEdit
{
    public static string Today(DateTime today) => today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Sección por nombre (sin distinguir mayúsculas ni tildes, sin emojis) o, si no, por tipo
    /// («pendientes» → «Por jugar»). Sin nombre: la de «Por jugar» (se crea si no existe).</summary>
    public static Section FindSection(GameDocument doc, string? name, bool create = true)
    {
        if (name == null)
        {
            var backlog = doc.Sections.FirstOrDefault(s => s.Kind == SectionKind.Backlog);
            if (backlog != null) return backlog;
            if (!create) throw new ListEditException("La lista no tiene sección «Por jugar».");
            var s = new Section { Name = ImportPlanner.DefaultNames[SectionKind.Backlog] };
            doc.Sections.Add(s);
            return s;
        }
        var key = SectionKinds.Normalize(SectionKinds.StripLeadingSymbols(name));
        var exact = doc.Sections.FirstOrDefault(s => SectionKinds.Normalize(SectionKinds.StripLeadingSymbols(s.Name)) == key);
        if (exact != null) return exact;
        var kind = SectionKinds.Infer(name);
        var byKind = kind == SectionKind.Custom ? null : doc.Sections.FirstOrDefault(s => s.Kind == kind);
        return byKind ?? throw new ListEditException(
            $"No hay ninguna sección «{name.Trim()}». Secciones de la lista: {string.Join(", ", doc.Sections.Select(s => $"«{s.Name}»"))}.");
    }

    /// <summary>Saga o género de la sección con ese nombre; si no existe se crea al final («Nombre:»), antes de las notas separadoras.
    /// Sin nombre: los juegos sueltos (al principio de la sección).</summary>
    public static Block EnsureBlock(Section s, string? name, BlockStyle style = BlockStyle.Colon)
    {
        if (string.IsNullOrWhiteSpace(name) || style == BlockStyle.None)
        {
            var loose = s.Blocks.FirstOrDefault(b => b.Type == BlockType.Category && b.Style == BlockStyle.None);
            if (loose != null) return loose;
            loose = new Block { Style = BlockStyle.None };
            s.Blocks.Insert(0, loose);
            return loose;
        }
        var key = SectionKinds.Normalize(name);
        var found = s.Blocks.FirstOrDefault(b => b.Type == BlockType.Category && b.Style != BlockStyle.None && SectionKinds.Normalize(b.Name) == key);
        if (found != null) return found;
        var block = new Block { Name = name.Trim(), Style = style };
        var at = s.Blocks.Count;
        while (at > 0 && s.Blocks[at - 1].Type == BlockType.Divider) at--;
        s.Blocks.Insert(at, block);
        return block;
    }

    public sealed record Located(Section Section, Block Block, GameEntry Entry);

    public static IEnumerable<Located> All(GameDocument doc) =>
        from s in doc.Sections from b in s.Blocks from e in b.Entries select new Located(s, b, e);

    /// <summary>Añade un juego. Falla si ya está en la lista (mismo título, en cualquier sección).</summary>
    public static Located Add(GameDocument doc, string title, string platform, string? section, string? saga, DateTime today)
    {
        title = title.Trim();
        if (title.Length == 0) throw new ListEditException("El título está vacío.");
        // Solo el mismo título escrito igual (sin contar mayúsculas, tildes ni espacios): «Resident Evil 4» y
        // «Resident Evil 4 (Remake)» son juegos distintos.
        var dup = All(doc).FirstOrDefault(x => Plain(x.Entry.Title) == Plain(title));
        if (dup != null) throw new ListEditException($"«{dup.Entry.Title.Trim()}» ya está en la lista, en «{dup.Section.Name}».");

        var s = FindSection(doc, section);
        var b = EnsureBlock(s, saga);
        var e = new GameEntry { Title = title, Platforms = NormalizePlatforms(platform) };
        if (SectionKinds.IsDone(s.Kind)) e.Date = Today(today);
        b.Entries.Add(e);
        return new Located(s, b, e);
    }

    /// <summary>«PC/Steam Deck» → «PC / Steam Deck», como lo escribe la app.</summary>
    public static string? NormalizePlatforms(string? p)
    {
        var parts = (p ?? "").Split('/').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        return parts.Count == 0 ? null : string.Join(" / ", parts);
    }

    /// <summary>Título para comparar escrito igual: sin mayúsculas, tildes ni espacios de más (los paréntesis cuentan).</summary>
    public static string Plain(string title) => string.Join(' ', SectionKinds.Normalize(title).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Juegos que encajan con el texto, del criterio más estricto al más amplio: título igual (con paréntesis),
    /// título igual sin signos ni etiquetas como «(Remake)», o título que contiene el texto. Se queda en el primer criterio que da algo.</summary>
    public static List<Located> Candidates(GameDocument doc, string query, Func<Located, bool>? filter = null)
    {
        var all = All(doc).Where(x => filter?.Invoke(x) ?? true).ToList();
        var plain = Plain(query);
        var key = TitleMatch.Key(query);
        if (key.Length == 0 && plain.Length == 0) throw new ListEditException("El título está vacío.");
        var tiers = new Func<Located, bool>[]
        {
            x => Plain(x.Entry.Title) == plain,
            x => key.Length > 0 && TitleMatch.Key(x.Entry.Title) == key,
            x => key.Length > 0 && (" " + TitleMatch.Key(x.Entry.Title) + " ").Contains(" " + key + " ", StringComparison.Ordinal),
            x => key.Length > 0 && TitleMatch.Key(x.Entry.Title).Contains(key, StringComparison.Ordinal)
        };
        foreach (var tier in tiers)
        {
            var hit = all.Where(tier).ToList();
            if (hit.Count > 0) return hit;
        }
        return new List<Located>();
    }

    /// <summary>Un único juego para el texto dado; si no hay ninguno o hay varios, falla con un mensaje claro.</summary>
    public static Located Find(GameDocument doc, string query, Func<Located, bool>? filter = null) => Single(Candidates(doc, query, filter), query);

    private static Located Single(List<Located> pool, string query)
    {
        if (pool.Count == 1) return pool[0];
        if (pool.Count == 0) throw new ListEditException($"No hay ningún juego que se llame «{query.Trim()}».");
        throw new ListEditException($"«{query.Trim()}» coincide con {pool.Count} juegos: " +
            string.Join(", ", pool.Take(8).Select(x => $"«{x.Entry.Title.Trim()}» ({x.Section.Name})")) + (pool.Count > 8 ? "…" : "") + ". Escribe el título completo.");
    }

    /// <summary>Lleva el juego a Completados (la primera sección de ese tipo), a su misma saga o género, con la fecha de hoy si no tenía.
    /// Devuelve null si ya estaba completado (no cambia nada).</summary>
    public static Located? Complete(GameDocument doc, string query, DateTime today)
    {
        var target = doc.Sections.FirstOrDefault(s => s.Kind == SectionKind.Completed)
                     ?? throw new ListEditException("La lista no tiene sección de Completados.");
        // Si el título está a la vez pendiente y completado (p. ej. un remake), se prefiere el pendiente.
        var pending = Candidates(doc, query, l => !SectionKinds.IsDone(l.Section.Kind));
        if (pending.Count == 0)
        {
            // ¿Ya está completado? Entonces no hay nada que hacer; si tampoco está, se cuenta por qué.
            var done = Single(Candidates(doc, query), query);
            return SectionKinds.IsDone(done.Section.Kind) ? null : done;
        }
        var x = Single(pending, query);
        x.Block.Entries.Remove(x.Entry);
        var block = EnsureBlock(target, x.Block.Name, x.Block.Style == BlockStyle.Paren ? BlockStyle.Paren : x.Block.Style == BlockStyle.Colon ? BlockStyle.Colon : BlockStyle.None);
        block.Entries.Add(x.Entry);
        if (string.IsNullOrWhiteSpace(x.Entry.Date)) x.Entry.Date = Today(today);
        return new Located(target, block, x.Entry);
    }
}
