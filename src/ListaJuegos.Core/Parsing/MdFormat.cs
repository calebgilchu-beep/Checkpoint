using System.Text;
using System.Text.RegularExpressions;

namespace ListaJuegos.Core.Parsing;

/// <summary>Formato Markdown enriquecido: front matter, # título, ## secciones con emoji y recuento,
/// ### categorías (o &lt;details&gt; plegables), listas de tareas, enlaces a tiendas, portadas, tabla resumen.</summary>
public static partial class MdFormat
{
    private const string ImplicitMarker = "<!-- lj:implicit -->";
    private const string Sep = " · ";
    private const string Dash = " — ";

    [GeneratedRegex(@"\s\((\d+)\)$")] private static partial Regex CountRx();
    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex ImgRx();
    [GeneratedRegex(@"^\s*[-*+]\s+(?:\[( |x|X)\]\s+)?")] private static partial Regex BulletRx();
    [GeneratedRegex(@"^_?Actualizado el (\d{1,2}/\d{1,2}/\d{2,4})_?$", RegexOptions.IgnoreCase)] private static partial Regex UpdatedRx();
    [GeneratedRegex(@"<summary>\s*(?:<strong>|<b>)?(.*?)(?:</strong>|</b>)?(?:\s*\(\d+\))?\s*</summary>", RegexOptions.IgnoreCase)] private static partial Regex SummaryRx();
    [GeneratedRegex(@"\\([\\`*_\[\]~<>#|])")] private static partial Regex UnescapeRx();

    // ───────────────────────────── escritura ─────────────────────────────

    public static string Write(GameDocument doc)
    {
        var o = doc.Markdown;
        var L = new List<string>();
        var (title, updated) = TxtFormat.SplitHeader(doc.Header);

        if (o.FrontMatter)
        {
            L.Add("---");
            if (doc.HasHeader && title.Length > 0) L.Add($"titulo: \"{title.Replace("\"", "'")}\"");
            if (updated != null) L.Add($"actualizado: \"{updated}\"");
            L.Add($"lista-juegos: [{string.Join(", ", OptionNames(o))}]");
            L.Add("---");
            L.Add("");
        }

        if (doc.HasHeader && title.Length > 0)
        {
            L.Add("# " + (o.Emojis ? "🎮 " : "") + Escape(title));
            L.Add("");
        }
        if (updated != null)
        {
            L.Add($"> _Actualizado el {updated}_");
            L.Add("");
        }

        if (o.SummaryTable && doc.Sections.Count > 0)
        {
            L.Add("| Sección | Juegos |");
            L.Add("|:--|--:|");
            foreach (var s in doc.Sections)
                L.Add($"| {SectionLabel(s, o, false).Replace("|", "\\|")} | {s.Entries.Count()} |");
            L.Add("");
        }

        var first = true;
        foreach (var s in doc.Sections)
        {
            if (!first && o.Rules) { L.Add("---"); L.Add(""); }
            first = false;
            if (s.Implicit) L.Add(ImplicitMarker);
            L.Add("## " + SectionLabel(s, o, o.Counts));
            L.Add("");
            var kind = s.Kind;

            foreach (var b in s.Blocks)
            {
                if (b.Type == BlockType.Divider)
                {
                    L.Add(b.Style == BlockStyle.Bracket ? "> " + Escape(b.Name.Trim()) : Escape(b.Name.Trim()));
                    L.Add("");
                    continue;
                }

                var named = b.Style != BlockStyle.None;
                var catName = b.Style == BlockStyle.Paren ? $"({b.Name.Trim()})" : b.Name.Trim();
                if (named && o.Collapsible)
                {
                    L.Add("<details open>");
                    L.Add($"<summary><strong>{HtmlEscape(catName)}</strong> ({b.Entries.Count})</summary>");
                    L.Add("");
                }
                else if (named)
                {
                    L.Add("### " + catName);
                    L.Add("");
                }

                foreach (var e in b.Entries) L.Add(EntryLineMd(e, kind, o));
                if (b.Entries.Count > 0) L.Add("");
                if (named && o.Collapsible) { L.Add("</details>"); L.Add(""); }
            }
        }

        while (L.Count > 0 && L[^1].Length == 0) L.RemoveAt(L.Count - 1);
        var nl = doc.Layout.NewLine;
        return string.Join(nl, L) + nl;
    }

    private static IEnumerable<string> OptionNames(MarkdownOptions o)
    {
        if (o.Emojis) yield return "emojis";
        if (o.Counts) yield return "recuentos";
        if (o.SummaryTable) yield return "tabla";
        if (o.TaskList) yield return "tareas";
        if (o.StoreLinks) yield return "enlaces";
        if (o.Covers) yield return "portadas";
        if (o.Collapsible) yield return "plegables";
        if (o.Rules) yield return "separadores";
    }

    private static string SectionLabel(Section s, MarkdownOptions o, bool count)
    {
        var sb = new StringBuilder();
        if (o.Emojis) sb.Append(SectionKinds.Emoji(s.Kind)).Append(' ');
        sb.Append(s.Name.Trim());
        if (count) sb.Append(" (").Append(s.Entries.Count()).Append(')');
        return sb.ToString();
    }

    public static string EntryLineMd(GameEntry e, SectionKind kind, MarkdownOptions o)
    {
        e = EntryLine.Normalized(e);
        var sb = new StringBuilder("- ");
        if (o.TaskList) sb.Append(SectionKinds.IsDone(kind) ? "[x] " : "[ ] ");

        var fields = new List<string>(e.Fields);
        string? url = null;
        if (o.StoreLinks)
        {
            var idx = fields.FindIndex(f => Fields.Classify(f) == FieldKind.StoreId);
            if (idx >= 0)
            {
                var st = Fields.FirstStore(e)!.Value;
                url = Fields.StoreUrl(st.store, st.id);
                if (url != null) fields.RemoveAt(idx);
            }
        }

        var t = $"**{Escape(e.Title)}**";
        if (url != null) t = $"[{t}]({url})";
        if (kind == SectionKind.Abandoned) t = $"~~{t}~~";
        sb.Append(t);

        var hasPlat = !string.IsNullOrWhiteSpace(e.Platforms);
        if (e.Note != null && hasPlat && e.NotePos == NotePosition.BeforePlatform) sb.Append(" *").Append(Escape(e.Note)).Append('*');
        if (hasPlat) sb.Append(Dash).Append(Code(e.Platforms!));

        var segs = new List<string>();
        if (e.Note != null && !(hasPlat && e.NotePos == NotePosition.BeforePlatform)) segs.Add($"*{Escape(e.Note)}*");
        if (e.Date != null) segs.Add("📅 " + e.Date);
        foreach (var f in fields)
            segs.Add(Fields.Classify(f) switch
            {
                FieldKind.Hours => "⏱ " + f,
                FieldKind.Achievements => "🏅 " + f,
                FieldKind.Duration => "⌛ " + f.Trim(),
                _ => Escape(f)
            });
        foreach (var s in segs) sb.Append(Sep).Append(s);

        if (o.Covers)
        {
            var steam = Fields.StoreId(e, "steam");
            if (steam != null)
                sb.Append($" <img src=\"https://cdn.akamai.steamstatic.com/steam/apps/{steam}/library_600x900.jpg\" alt=\"\" width=\"48\" align=\"right\">");
        }
        return sb.ToString();
    }

    private static string Code(string s) => s.Contains('`') ? $"`` {s} ``" : $"`{s}`";

    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '~' or '<' or '>' or '|') sb.Append('\\');
            sb.Append(c);
        }
        // «#» solo es especial al principio.
        return sb.Length > 0 && sb[0] == '#' ? "\\" + sb : sb.ToString();
    }

    public static string Unescape(string s) => UnescapeRx().Replace(s, "$1");

    private static string HtmlEscape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    private static string HtmlUnescape(string s) => s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&");

    // ───────────────────────────── lectura ─────────────────────────────

    public static GameDocument Parse(string text)
    {
        var doc = new GameDocument { HasHeader = false };
        doc.Layout.SourceKind = FileKind.Md;
        var lines = TextLines.Split(text, doc.Layout);
        doc.Layout.TrailingBlankLines = 1;
        var o = doc.Markdown;

        var i = 0;
        string? fmUpdated = null, fmTitle = null;
        o.FrontMatter = false;
        if (lines.Count > 0 && lines[0].Trim() == "---")
        {
            var end = lines.FindIndex(1, l => l.Trim() == "---");
            if (end > 0)
            {
                o.FrontMatter = true;
                for (var k = 1; k < end; k++)
                {
                    var m = Regex.Match(lines[k], @"^\s*([\w-]+)\s*:\s*(.*)$");
                    if (!m.Success) continue;
                    var val = m.Groups[2].Value.Trim().Trim('"', '\'');
                    switch (m.Groups[1].Value.ToLowerInvariant())
                    {
                        case "titulo": fmTitle = val; break;
                        case "actualizado": fmUpdated = val; break;
                        case "lista-juegos":
                            var set = val.Trim('[', ']').Split(',').Select(x => x.Trim().ToLowerInvariant()).ToHashSet();
                            o.Emojis = set.Contains("emojis"); o.Counts = set.Contains("recuentos"); o.SummaryTable = set.Contains("tabla");
                            o.TaskList = set.Contains("tareas"); o.StoreLinks = set.Contains("enlaces"); o.Covers = set.Contains("portadas");
                            o.Collapsible = set.Contains("plegables"); o.Rules = set.Contains("separadores");
                            break;
                    }
                }
                i = end + 1;
            }
        }

        Section? section = null;
        Block? block = null;
        string? title = null, updated = fmUpdated;
        var pendingImplicit = false;
        var sawContent = false;

        Section Cur()
        {
            if (section != null) return section;
            section = new Section { Name = SectionKinds.DefaultFirstSection, Implicit = true };
            doc.Sections.Add(section);
            return section;
        }

        for (; i < lines.Count; i++)
        {
            var line = lines[i];
            var t = line.Trim();
            if (t.Length == 0) continue;
            if (t == ImplicitMarker) { pendingImplicit = true; continue; }
            if (Regex.IsMatch(t, @"^(-{3,}|\*{3,}|_{3,})$")) continue;
            if (t.StartsWith('|')) continue;
            if (Regex.IsMatch(t, @"^</?details\b[^>]*>$", RegexOptions.IgnoreCase)) { if (t.StartsWith("</")) block = null; continue; }
            if (t.StartsWith("<!--")) continue;

            var sm = SummaryRx().Match(t);
            if (sm.Success)
            {
                var name = HtmlUnescape(sm.Groups[1].Value.Trim());
                block = MakeCategory(name);
                Cur().Blocks.Add(block);
                continue;
            }

            if (t.StartsWith("# "))
            {
                if (title == null && !sawContent) { title = Unescape(SectionKinds.StripLeadingSymbols(t[2..].Trim())); continue; }
            }
            if (t.StartsWith("## "))
            {
                var name = SectionKinds.StripLeadingSymbols(t[3..].Trim());
                if (o.Counts) name = CountRx().Replace(name, "");
                section = new Section { Name = Unescape(name.Trim()), Implicit = pendingImplicit };
                pendingImplicit = false;
                doc.Sections.Add(section);
                block = null;
                sawContent = true;
                continue;
            }
            if (Regex.IsMatch(t, @"^#{3,6}\s"))
            {
                var name = Regex.Replace(t, @"^#{3,6}\s+", "").Trim();
                block = MakeCategory(Unescape(name));
                Cur().Blocks.Add(block);
                sawContent = true;
                continue;
            }
            if (t.StartsWith('>'))
            {
                var q = t.TrimStart('>').Trim();
                var um = UpdatedRx().Match(q);
                if (um.Success && !sawContent) { updated = um.Groups[1].Value; continue; }
                block = new Block { Type = BlockType.Divider, Style = BlockStyle.Bracket, Name = Unescape(q) };
                Cur().Blocks.Add(block);
                sawContent = true;
                continue;
            }
            var bm = BulletRx().Match(line);
            if (bm.Success)
            {
                var e = ParseEntry(line[bm.Length..]);
                if (block == null || block.Type != BlockType.Category)
                {
                    block = new Block { Type = BlockType.Category, Style = BlockStyle.None };
                    Cur().Blocks.Add(block);
                }
                block.Entries.Add(e);
                sawContent = true;
                continue;
            }

            block = new Block { Type = BlockType.Divider, Style = BlockStyle.Plain, Name = Unescape(t) };
            Cur().Blocks.Add(block);
            sawContent = true;
        }

        title ??= fmTitle;
        if (title != null)
        {
            doc.HasHeader = true;
            doc.Header = updated != null ? $"{title} (Actualizado el {updated})" : title;
        }
        return doc;
    }

    private static Block MakeCategory(string name)
    {
        name = name.Trim();
        if (name.Length >= 2 && name.StartsWith('(') && name.EndsWith(')'))
            return new Block { Type = BlockType.Category, Style = BlockStyle.Paren, Name = name[1..^1].Trim() };
        return new Block { Type = BlockType.Category, Style = BlockStyle.Colon, Name = name.TrimEnd(':').Trim() };
    }

    public static GameEntry ParseEntry(string body)
    {
        var e = new GameEntry();
        body = ImgRx().Replace(body, "").Trim();
        // Línea escrita a mano al estilo del .txt: «Título - Plataforma [nota] #fecha# {campo}».
        if (!Regex.IsMatch(body, @"^(~~)?\[?(\*\*|__)")) return EntryLine.Parse(body);
        var parts = body.Split(Sep);
        var main = parts[0];

        string titlePart = main;
        var di = IndexOfOutsideCode(main, Dash);
        if (di >= 0)
        {
            titlePart = main[..di];
            var plat = main[(di + Dash.Length)..].Trim();
            var codes = Regex.Matches(plat, @"``\s?(.+?)\s?``|`([^`]+)`");
            e.Platforms = codes.Count > 0
                ? string.Join("/", codes.Select(c => (c.Groups[1].Success ? c.Groups[1].Value : c.Groups[2].Value).Trim()))
                : plat;
            if (e.Platforms.Length == 0) e.Platforms = null;
        }

        titlePart = titlePart.Trim();
        var nm = Regex.Match(titlePart, @"\s\*((?:\\.|[^*])+)\*$");
        if (nm.Success && !titlePart.EndsWith("**"))
        {
            e.Note = Unescape(nm.Groups[1].Value.Trim());
            e.NotePos = NotePosition.BeforePlatform;
            titlePart = titlePart[..nm.Index].Trim();
        }
        if (titlePart.StartsWith("~~") && titlePart.EndsWith("~~") && titlePart.Length >= 4) titlePart = titlePart[2..^2].Trim();
        string? url = null;
        var lm = Regex.Match(titlePart, @"^\[(.+)\]\(([^)\s]+)\)$");
        if (lm.Success) { titlePart = lm.Groups[1].Value.Trim(); url = lm.Groups[2].Value; }
        if (titlePart.StartsWith("**") && titlePart.EndsWith("**") && titlePart.Length >= 4) titlePart = titlePart[2..^2];
        else if (titlePart.StartsWith("__") && titlePart.EndsWith("__") && titlePart.Length >= 4) titlePart = titlePart[2..^2];
        e.Title = Unescape(titlePart.Trim());

        foreach (var raw in parts.Skip(1))
        {
            var s = raw.Trim();
            if (s.Length == 0) continue;
            if (s.StartsWith("📅")) { e.Date = s["📅".Length..].Trim(); continue; }
            if (s.Length >= 2 && s.StartsWith('*') && s.EndsWith('*') && !s.StartsWith("**"))
            {
                e.Note = Unescape(s[1..^1].Trim());
                e.NotePos = NotePosition.AfterPlatform;
                continue;
            }
            if (s.StartsWith("⏱")) s = s["⏱".Length..].TrimStart('️').Trim();
            else if (s.StartsWith("🏅")) s = s["🏅".Length..].Trim();
            else if (s.StartsWith("⌛")) s = s["⌛".Length..].TrimStart('️').Trim();
            e.Fields.Add(Unescape(s));
        }
        if (url != null && Fields.FromStoreUrl(url) is { } st) e.Fields.Add($"{st.store}:{st.id}");
        if (e.Platforms == null && e.Note != null) e.NotePos = NotePosition.AfterPlatform;
        return e;
    }

    private static int IndexOfOutsideCode(string s, string needle)
    {
        var inCode = false;
        for (var i = 0; i <= s.Length - needle.Length; i++)
        {
            if (s[i] == '`') inCode = !inCode;
            if (!inCode && string.CompareOrdinal(s, i, needle, 0, needle.Length) == 0) return i;
        }
        return -1;
    }
}
