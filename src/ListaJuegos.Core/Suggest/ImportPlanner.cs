using ListaJuegos.Core.Connectors;

namespace ListaJuegos.Core.Suggest;

public static class ImportPlanner
{
    public static readonly Dictionary<SectionKind, string> DefaultNames = new()
    {
        [SectionKind.Completed] = "Completados",
        [SectionKind.Hundred] = "Completados al 100%",
        [SectionKind.Playing] = "Jugando ahora",
        [SectionKind.Unfinished] = "Inconclusos",
        [SectionKind.Backlog] = "Por jugar",
        [SectionKind.Abandoned] = "Abandonados",
        [SectionKind.Wishlist] = "Lista de deseos"
    };

    /// <summary>Nombre de la sección de ese tipo que ya existe en el documento, o el nombre por defecto.</summary>
    public static string SectionName(GameDocument doc, SectionKind kind) =>
        doc.Sections.FirstOrDefault(s => s.Kind == kind)?.Name ?? DefaultNames.GetValueOrDefault(kind, "Otros");

    public static (string? name, bool paren) SuggestCategory(GameDocument doc, string title, IEnumerable<string>? genres = null)
    {
        var key = TitleMatch.Key(title);
        var saga = Sagas.Find(title);
        var cats = doc.Sections.SelectMany(s => s.Blocks)
            .Where(b => b.Type == BlockType.Category && b.Style != BlockStyle.None && b.Name.Trim().Length > 0).ToList();

        foreach (var c in cats)
        {
            var ck = TitleMatch.Key(c.Name);
            if (ck.Length > 0 && (key == ck || key.StartsWith(ck + " "))) return (c.Name, c.Style == BlockStyle.Paren);
        }
        if (saga != null)
        {
            var sk = TitleMatch.Key(saga);
            foreach (var c in cats)
                if (TitleMatch.Key(c.Name) == sk || Sagas.Find(c.Name) == saga) return (c.Name, c.Style == BlockStyle.Paren);
            return (saga, false);
        }
        if (genres != null && genres.Any(g => g.Contains("Indie", StringComparison.OrdinalIgnoreCase)))
        {
            var ind = cats.FirstOrDefault(c => TitleMatch.Key(c.Name).StartsWith("indie"));
            return ind != null ? (ind.Name, ind.Style == BlockStyle.Paren) : ("Indies", true);
        }
        return (null, false);
    }

    public static SectionKind SuggestKind(ImportCandidate g, DateTime now)
    {
        if (g.Achievements >= 99.5) return SectionKind.Hundred;
        if (g.LastPlayed is { } lp && (now - lp).TotalDays <= 21) return SectionKind.Playing;
        if (g.Beaten) return SectionKind.Completed;            // RetroAchievements: juego superado
        if (g.Hours > 0 || g.Achievements > 0) return SectionKind.Unfinished;
        return SectionKind.Backlog;
    }

    /// <summary>Une los resultados de varias fuentes (p. ej. Steam local + Steam online) sin repetir juegos.</summary>
    public static List<ImportCandidate> Merge(IEnumerable<ImportCandidate> all)
    {
        var result = new List<ImportCandidate>();
        foreach (var g in all)
        {
            var same = result.FirstOrDefault(r =>
                (r.StoreKey != null && r.StoreKey == g.StoreKey && r.StoreId == g.StoreId) ||
                (r.Source == g.Source && TitleMatch.Key(r.Title) == TitleMatch.Key(g.Title)));
            if (same == null) { result.Add(g); continue; }
            same.Hours ??= g.Hours;
            if (g.Hours > same.Hours) same.Hours = g.Hours;
            same.Achievements ??= g.Achievements;
            if (g.LastPlayed > same.LastPlayed || same.LastPlayed == null) same.LastPlayed = g.LastPlayed ?? same.LastPlayed;
            same.Installed |= g.Installed;
            same.CoverUrl ??= g.CoverUrl;
            same.LocalImage ??= g.LocalImage;
            same.StoreId ??= g.StoreId; same.StoreKey ??= g.StoreKey;
        }
        return result;
    }

    public static void Plan(GameDocument doc, List<ImportCandidate> games, DateTime now)
    {
        var entries = doc.Sections.SelectMany(s => s.Entries.Select(e => (s, e))).ToList();
        foreach (var g in games)
        {
            var kind = SuggestKind(g, now);
            g.SuggestedSection = SectionName(doc, kind);
            var (cat, paren) = SuggestCategory(doc, g.Title, g.Genres);
            g.SuggestedCategory = cat;
            g.SuggestedParen = paren;

            var dup = entries.FirstOrDefault(x =>
                (g.StoreKey != null && g.StoreId != null && Fields.StoreId(x.e, g.StoreKey) == g.StoreId) ||
                TitleMatch.IsSameGame(x.e.Title, g.Title));
            g.DuplicateOf = dup.s?.Name;
            g.DuplicateEntryId = dup.e?.Id;
            g.Selected = dup.e == null;
        }
    }

    /// <summary>Crea la entrada para un juego importado.</summary>
    public static GameEntry ToEntry(ImportCandidate g, bool writeStoreId, DateTime now)
    {
        var e = new GameEntry { Title = g.Title.Trim(), Platforms = g.Platform };
        if (!string.IsNullOrWhiteSpace(g.Note))
        {
            // Como en la lista del usuario: «-Super Mario Bros [emulación] - GBA/SNES/NES»
            e.Note = g.Note.Trim();
            e.NotePos = string.IsNullOrWhiteSpace(g.Platform) ? NotePosition.AfterPlatform : NotePosition.BeforePlatform;
        }
        if (g.Hours > 0) e.Fields.Add(Fields.Make(FieldKind.Hours, g.Hours.Value));
        if (g.Achievements is { } a) e.Fields.Add(Fields.Make(FieldKind.Achievements, a));
        if (g.Hardcore is > 0) e.Fields.Add(HardcoreField(g.Hardcore.Value));
        if (writeStoreId && g.StoreKey != null && g.StoreId != null) e.Fields.Add($"{g.StoreKey}:{g.StoreId}");
        if (SectionKinds.IsDone(SectionKinds.Infer(g.SuggestedSection ?? ""))) e.Date = now.ToString("dd/MM/yyyy");
        return e;
    }

    /// <summary>Actualiza horas y logros de los juegos ya enlazados con una tienda. Devuelve cuántos cambiaron.</summary>
    public static int RefreshLinked(GameDocument doc, IEnumerable<ImportCandidate> games)
    {
        var changed = 0;
        var byKey = games.Where(g => g.StoreKey != null && g.StoreId != null)
                         .GroupBy(g => g.StoreKey + ":" + g.StoreId).ToDictionary(x => x.Key, x => x.First());
        foreach (var e in doc.Sections.SelectMany(s => s.Entries))
        {
            // Cualquiera de sus tiendas: un juego puede tener {steam:…} y también {ra:…}.
            var g = Fields.AllStores(e).Select(st => byKey.GetValueOrDefault(st.store + ":" + st.id)).FirstOrDefault(x => x != null);
            if (g == null) continue;
            var before = string.Join("|", e.Fields);
            if (g.Hours > 0) Fields.Set(e, FieldKind.Hours, Fields.Make(FieldKind.Hours, g.Hours.Value));
            if (g.Achievements is { } a) Fields.Set(e, FieldKind.Achievements, Fields.Make(FieldKind.Achievements, a));
            if (g.Hardcore is > 0)
            {
                var i = e.Fields.FindIndex(IsHardcoreField);
                if (i >= 0) e.Fields[i] = HardcoreField(g.Hardcore.Value); else e.Fields.Add(HardcoreField(g.Hardcore.Value));
            }
            if (before != string.Join("|", e.Fields)) changed++;
        }
        return changed;
    }

    /// <summary>{Hardcore: 85%}: logros de RetroAchievements desbloqueados en modo Hardcore (campo libre clave: valor).</summary>
    public static string HardcoreField(double percent) => $"Hardcore: {Fields.FormatNumber(percent)}%";

    public static bool IsHardcoreField(string f) => f.TrimStart().StartsWith("Hardcore:", StringComparison.OrdinalIgnoreCase);
}
