using ListaJuegos.Core.Connectors;

namespace ListaJuegos.Core.Suggest;

/// <summary>Duración según la historia principal de {ttb: …}: corto &lt; 8 h, medio 8–20 h, largo &gt; 20 h.</summary>
public enum GameLength { Any, Short, Medium, Long }

/// <summary>Filtros de «¿Qué juego ahora?». Platform null = cualquiera.</summary>
public sealed record NextFilter(GameLength Length = GameLength.Any, bool SagaOnly = false, string? Platform = null);

public sealed class NextPick
{
    public string EntryId { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Platforms { get; init; }
    public string Section { get; init; } = "";
    public double? Hours { get; init; }               // historia principal
    public double? FullHours { get; init; }           // completista
    public GameLength Length { get; init; }           // Any = sin duración anotada
    public string? Saga { get; init; }
    public int SagaDone { get; init; }                // entregas de la saga en Completados (y al 100%)
    public int SagaStarted { get; init; }             // entregas en Inconclusos o Jugando ahora
    public List<string> SagaTitles { get; init; } = new();
}

/// <summary>Sugerencias de la sección «Por jugar»: por tiempo disponible, por sagas ya empezadas y por plataforma.</summary>
public static class NextGame
{
    public const double ShortMax = 8, MediumMax = 20;

    public static GameLength LengthOf(double? hours) => hours switch
    {
        null => GameLength.Any,
        < ShortMax => GameLength.Short,
        <= MediumMax => GameLength.Medium,
        _ => GameLength.Long
    };

    /// <summary>Saga de un juego: su saga «Nombre:» en la lista o, si está suelto o en un género «(…)», la del diccionario.</summary>
    public static string? SagaOf(Block b, GameEntry e)
    {
        if (b.Type == BlockType.Category && b.Style == BlockStyle.Colon && b.Name.Trim().Length > 0) return b.Name.Trim();
        return Sagas.Find(e.Title);
    }

    /// <summary>Clave para comparar sagas: «Resident Evil», «RESIDENT EVIL» y «Resident Evil 4» (por el diccionario) coinciden.</summary>
    public static string SagaKey(string saga) => TitleMatch.Key(Sagas.Find(saga) ?? saga);

    public static List<NextPick> Suggest(GameDocument doc, NextFilter filter)
    {
        // Sagas con entregas jugadas: completadas o empezadas.
        var played = new Dictionary<string, (int done, int started, List<string> titles)>();
        foreach (var s in doc.Sections)
        {
            var done = s.Kind is SectionKind.Completed or SectionKind.Hundred;
            var started = s.Kind is SectionKind.Unfinished or SectionKind.Playing;
            if (!done && !started) continue;
            foreach (var b in s.Blocks)
                foreach (var e in b.Entries)
                {
                    if (SagaOf(b, e) is not { } saga) continue;
                    var k = SagaKey(saga);
                    if (k.Length == 0) continue;
                    (int done, int started, List<string> titles) cur = played.TryGetValue(k, out var v) ? v : (0, 0, new List<string>());
                    cur.titles.Add(e.Title.Trim());
                    played[k] = done ? (cur.done + 1, cur.started, cur.titles) : (cur.done, cur.started + 1, cur.titles);
                }
        }

        var picks = new List<(NextPick pick, int order)>();
        var order = 0;
        foreach (var s in doc.Sections.Where(s => s.Kind == SectionKind.Backlog))
            foreach (var b in s.Blocks)
                foreach (var e in b.Entries)
                {
                    order++;
                    if (filter.Platform is { Length: > 0 } p && !HasPlatform(e, p)) continue;
                    var d = Fields.Duration(e);
                    var len = LengthOf(d?.main);
                    if (filter.Length != GameLength.Any && len != filter.Length) continue;
                    var saga = SagaOf(b, e);
                    var info = saga != null ? played.GetValueOrDefault(SagaKey(saga)) : default;
                    var inSaga = info.done + info.started > 0;
                    if (filter.SagaOnly && !inSaga) continue;
                    picks.Add((new NextPick
                    {
                        EntryId = e.Id, Title = e.Title.Trim(), Platforms = e.Platforms, Section = s.Name,
                        Hours = d?.main, FullHours = d?.full, Length = len,
                        Saga = inSaga ? saga : null, SagaDone = info.done, SagaStarted = info.started,
                        SagaTitles = inSaga ? info.titles : new()
                    }, order));
                }

        // Primero las continuaciones de sagas (cuantas más entregas jugadas, antes), luego los que tienen duración
        // (los más cortos antes) y, a igualdad, el orden de la lista.
        return picks
            .OrderByDescending(x => x.pick.SagaDone + x.pick.SagaStarted > 0)
            .ThenByDescending(x => x.pick.SagaDone * 2 + x.pick.SagaStarted)
            .ThenBy(x => x.pick.Hours == null)
            .ThenBy(x => x.pick.Hours ?? 0)
            .ThenBy(x => x.order)
            .Select(x => x.pick).ToList();
    }

    /// <summary>Horas estimadas de la sección: suma de la historia principal, juegos con duración y juegos en total.</summary>
    public static (double hours, int withDuration, int total) EstimatedHours(Section s)
    {
        double sum = 0; int with = 0, total = 0;
        foreach (var e in s.Entries)
        {
            total++;
            if (Fields.Duration(e) is { } d) { sum += d.main; with++; }
        }
        return (sum, with, total);
    }

    private static bool HasPlatform(GameEntry e, string platform) =>
        (e.Platforms ?? "").Split('/').Any(x => string.Equals(x.Trim(), platform.Trim(), StringComparison.CurrentCultureIgnoreCase));
}
