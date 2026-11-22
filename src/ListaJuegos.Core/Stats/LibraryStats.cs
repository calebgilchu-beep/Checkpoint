using System.Globalization;
using System.Text.RegularExpressions;
using ListaJuegos.Core.Connectors;
using ListaJuegos.Core.Suggest;

namespace ListaJuegos.Core.Stats;

/// <summary>Una fila del gráfico por años: juegos completados ese año, sus horas y su nota media.</summary>
public sealed record YearRow(int Year, int Games, double Hours, double? Rating, int Rated);

/// <summary>Una fila de un reparto (plataformas, sagas): nombre tal y como está escrito y número de juegos.</summary>
public sealed record CountRow(string Name, int Games);

public sealed class LibraryStatsResult
{
    public int Total { get; init; }
    public int Completed { get; init; }
    public int Dated { get; init; }
    public int Undated { get; init; }
    /// <summary>Horas jugadas ({42 h}) de todos los juegos de la lista.</summary>
    public double Hours { get; init; }
    public int WithHours { get; init; }
    /// <summary>Nota media ({★ 9/10}) de todos los juegos puntuados.</summary>
    public double? Rating { get; init; }
    public int Rated { get; init; }
    /// <summary>Años con juegos completados, de menor a mayor y sin huecos (los años vacíos van con 0).</summary>
    public List<YearRow> Years { get; init; } = new();
    /// <summary>Plataforma principal de los juegos completados, de más a menos.</summary>
    public List<CountRow> Platforms { get; init; } = new();
    public int NoPlatform { get; init; }
    /// <summary>Sagas con juegos completados, de más a menos.</summary>
    public List<CountRow> Sagas { get; init; } = new();
}

public sealed record YearGame(string EntryId, string Title, string? Platforms, string Date, double? Hours, double? Rating, bool Hundred);

public sealed record YearSummary(int Year, int Games, double Hours, double? Rating, List<YearGame> List, List<YearGame> Top, List<int> Available);

/// <summary>Estadísticas calculadas a partir del documento abierto (sin red ni archivos aparte).</summary>
public static partial class LibraryStats
{
    [GeneratedRegex(@"^(\d{1,2})[/.-](\d{1,2})[/.-](\d{2}|\d{4})$")] private static partial Regex DmyRx();
    [GeneratedRegex(@"^(\d{4})-(\d{1,2})-(\d{1,2})$")] private static partial Regex IsoRx();
    [GeneratedRegex(@"^(\d{1,2})[/.-](\d{4})$")] private static partial Regex MyRx();
    [GeneratedRegex(@"^(\d{4})$")] private static partial Regex YRx();

    /// <summary>Fecha de un juego como está escrita (#dd/mm/aaaa#, #dd/mm/aa#, #aaaa-mm-dd#, #mm/aaaa#, #aaaa#) → (año, mes, día).
    /// Mes y día son 0 si no constan.</summary>
    public static (int year, int month, int day)? ParseDate(string? date)
    {
        var s = (date ?? "").Trim();
        if (s.Length == 0) return null;
        int y, mo = 0, d = 0;
        Match m;
        if ((m = DmyRx().Match(s)).Success)
        {
            d = int.Parse(m.Groups[1].Value); mo = int.Parse(m.Groups[2].Value); y = int.Parse(m.Groups[3].Value);
            if (m.Groups[3].Value.Length == 2) y += y <= DateTime.Now.Year % 100 + 1 ? 2000 : 1900;
        }
        else if ((m = IsoRx().Match(s)).Success) { y = int.Parse(m.Groups[1].Value); mo = int.Parse(m.Groups[2].Value); d = int.Parse(m.Groups[3].Value); }
        else if ((m = MyRx().Match(s)).Success) { mo = int.Parse(m.Groups[1].Value); y = int.Parse(m.Groups[2].Value); }
        else if ((m = YRx().Match(s)).Success) y = int.Parse(m.Groups[1].Value);
        else return null;
        if (y is < 1970 or > 2100 || mo is < 0 or > 12 || d is < 0 or > 31) return null;
        return (y, mo, d);
    }

    public static int? YearOf(string? date) => ParseDate(date)?.year;

    private static bool IsCompleted(Section s) => s.Kind is SectionKind.Completed or SectionKind.Hundred;

    /// <summary>Juegos completados (secciones Completados y Completados al 100 %), con su bloque.</summary>
    public static IEnumerable<(Section s, Block b, GameEntry e)> CompletedEntries(GameDocument doc) =>
        from s in doc.Sections where IsCompleted(s)
        from b in s.Blocks
        from e in b.Entries
        select (s, b, e);

    /// <summary>Primera plataforma del juego («PC / Steam Deck» → «PC»).</summary>
    public static string? MainPlatform(GameEntry e) =>
        (e.Platforms ?? "").Split('/').Select(p => p.Trim()).FirstOrDefault(p => p.Length > 0);

    public static LibraryStatsResult Compute(GameDocument doc)
    {
        var all = doc.Sections.SelectMany(s => s.Entries).ToList();
        var done = CompletedEntries(doc).ToList();

        var hours = all.Select(e => Fields.Number(e, FieldKind.Hours)).Where(v => v != null).Select(v => v!.Value).ToList();
        var ratings = all.Select(e => Fields.Number(e, FieldKind.Rating)).Where(v => v != null).Select(v => v!.Value).ToList();

        // Por años
        var byYear = new SortedDictionary<int, List<GameEntry>>();
        foreach (var (_, _, e) in done)
            if (YearOf(e.Date) is { } y) (byYear.TryGetValue(y, out var l) ? l : byYear[y] = new()).Add(e);
        var years = new List<YearRow>();
        if (byYear.Count > 0)
            for (var y = byYear.Keys.First(); y <= byYear.Keys.Last(); y++)
                years.Add(Row(y, byYear.TryGetValue(y, out var l) ? l : new()));

        // Plataformas: la principal de cada juego, sin distinguir mayúsculas («pc» y «PC» van juntas)
        var plats = Count(done.Select(x => MainPlatform(x.e)), p => p.ToLowerInvariant());
        // Sagas: la saga «Nombre:» de la lista o, si está suelto o en un género, la del diccionario
        var sagas = Count(done.Select(x => NextGame.SagaOf(x.b, x.e)), NextGame.SagaKey);

        var dated = byYear.Values.Sum(l => l.Count);
        return new LibraryStatsResult
        {
            Total = all.Count,
            Completed = done.Count,
            Dated = dated,
            Undated = done.Count - dated,
            Hours = hours.Sum(),
            WithHours = hours.Count,
            Rating = ratings.Count > 0 ? Math.Round(ratings.Average(), 2) : null,
            Rated = ratings.Count,
            Years = years,
            Platforms = plats,
            NoPlatform = done.Count(x => MainPlatform(x.e) == null),
            Sagas = sagas
        };
    }

    private static YearRow Row(int year, List<GameEntry> games)
    {
        var r = games.Select(e => Fields.Number(e, FieldKind.Rating)).Where(v => v != null).Select(v => v!.Value).ToList();
        var h = games.Sum(e => Fields.Number(e, FieldKind.Hours) ?? 0);
        return new YearRow(year, games.Count, h, r.Count > 0 ? Math.Round(r.Average(), 2) : null, r.Count);
    }

    /// <summary>Cuenta agrupando por clave; muestra el nombre tal y como aparece la primera vez.
    /// Orden: más juegos primero y, a igualdad, por nombre.</summary>
    private static List<CountRow> Count(IEnumerable<string?> names, Func<string, string> key)
    {
        var map = new Dictionary<string, (string name, int n)>();
        foreach (var raw in names)
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            var k = key(name);
            if (k.Length == 0) continue;
            map[k] = map.TryGetValue(k, out var v) ? (v.name, v.n + 1) : (name, 1);
        }
        return map.Values.OrderByDescending(v => v.n).ThenBy(v => v.name, StringComparer.CurrentCultureIgnoreCase)
            .Select(v => new CountRow(v.name, v.n)).ToList();
    }

    /// <summary>Años con juegos completados con fecha, del más reciente al más antiguo.</summary>
    public static List<int> AvailableYears(GameDocument doc) =>
        CompletedEntries(doc).Select(x => YearOf(x.e.Date)).Where(y => y != null).Select(y => y!.Value).Distinct().OrderDescending().ToList();

    /// <summary>Datos del «Resumen del año»: juegos completados ese año en orden de fecha, totales y mejores puntuaciones.</summary>
    public static YearSummary Year(GameDocument doc, int year, int top = 3)
    {
        var games = CompletedEntries(doc)
            .Select((x, i) => (x.s, x.e, i, d: ParseDate(x.e.Date)))
            .Where(x => x.d?.year == year)
            .OrderBy(x => x.d!.Value.month == 0 ? 13 : x.d!.Value.month).ThenBy(x => x.d!.Value.day).ThenBy(x => x.i)
            .Select(x => new YearGame(x.e.Id, x.e.Title.Trim(), x.e.Platforms?.Trim(), x.e.Date!.Trim(),
                Fields.Number(x.e, FieldKind.Hours), Fields.Number(x.e, FieldKind.Rating), x.s.Kind == SectionKind.Hundred))
            .ToList();
        var rated = games.Where(g => g.Rating != null).ToList();
        var best = rated.Select((g, i) => (g, i)).OrderByDescending(x => x.g.Rating).ThenBy(x => x.i).Take(top).Select(x => x.g).ToList();
        return new YearSummary(year, games.Count, games.Sum(g => g.Hours ?? 0),
            rated.Count > 0 ? Math.Round(rated.Average(g => g.Rating!.Value), 2) : null, games, best, AvailableYears(doc));
    }
}
