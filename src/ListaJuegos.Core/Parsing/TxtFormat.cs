using System.Text.RegularExpressions;

namespace ListaJuegos.Core.Parsing;

internal enum Node { Start, Header, SectionHeader, CategoryHeader, Divider, Entry }

/// <summary>Formato .txt original: cabecera, &lt;Sección&gt;, Categoría:, (Categoría), [separador], -juegos.</summary>
public static class TxtFormat
{
    // ───────────────────────────── lectura ─────────────────────────────

    public static GameDocument Parse(string text)
    {
        var doc = new GameDocument();
        var lay = doc.Layout;
        lay.SourceKind = FileKind.Txt;
        var lines = TextLines.Split(text, lay);

        var gaps = new Dictionary<string, List<int>>();
        void Observe(string key, int v) { if (!gaps.TryGetValue(key, out var l)) gaps[key] = l = new(); l.Add(v); }
        var dashSpace = 0; var dashTight = 0;

        Section? section = null;
        Block? block = null;
        var prev = Node.Start;
        var blank = 0;
        var headerDone = false;

        Section CurrentSection()
        {
            if (section != null) return section;
            section = new Section { Name = SectionKinds.DefaultFirstSection, Implicit = true };
            doc.Sections.Add(section);
            return section;
        }

        foreach (var line in lines)
        {
            if (line.Trim().Length == 0) { blank++; continue; }
            var t = line.Trim();

            if (!headerDone)
            {
                headerDone = true;
                if (!IsStructural(t))
                {
                    doc.Header = line;
                    doc.HasHeader = true;
                    prev = Node.Header; blank = 0;
                    continue;
                }
                doc.HasHeader = false;
                doc.Header = "";
            }

            Node kind;
            if (t.StartsWith('-'))
            {
                kind = Node.Entry;
                var body = t[1..];
                if (body.StartsWith(' ')) dashSpace++; else dashTight++;
                var e = EntryLine.Parse(body);
                e.Raw = line;
                if (block == null || block.Type != BlockType.Category)
                {
                    block = new Block { Type = BlockType.Category, Style = BlockStyle.None };
                    CurrentSection().Blocks.Add(block);
                }
                block.Entries.Add(e);
            }
            else if (TryParseSection(t, out var sname))
            {
                kind = Node.SectionHeader;
                section = new Section { Name = sname, Raw = line };
                doc.Sections.Add(section);
                block = null;
            }
            else if (TryParseBlock(t, out var b))
            {
                b.Raw = line;
                kind = b.Type == BlockType.Divider ? Node.Divider : Node.CategoryHeader;
                CurrentSection().Blocks.Add(b);
                block = b;
            }
            else
            {
                kind = Node.Divider;
                block = new Block { Type = BlockType.Divider, Style = BlockStyle.Plain, Name = t, Raw = line };
                CurrentSection().Blocks.Add(block);
            }

            if (prev != Node.Start) Observe(GapKey(prev, kind), blank);
            prev = kind; blank = 0;
        }

        // La primera sección (sin línea propia) existe siempre, aunque esté vacía.
        if (doc.Sections.Count == 0 || !doc.Sections[0].Implicit)
            doc.Sections.Insert(0, new Section { Name = SectionKinds.DefaultFirstSection, Implicit = true });

        int Mode(string key, int def) =>
            gaps.TryGetValue(key, out var l) && l.Count > 0
                ? l.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key
                : def;
        lay.GapAfterHeader = Mode(nameof(lay.GapAfterHeader), 1);
        lay.GapAfterSection = Mode(nameof(lay.GapAfterSection), 1);
        lay.GapAfterCategory = Mode(nameof(lay.GapAfterCategory), 1);
        lay.GapBetweenEntries = Mode(nameof(lay.GapBetweenEntries), 0);
        lay.GapBeforeHeading = Mode(nameof(lay.GapBeforeHeading), 1);
        lay.EntryPrefix = dashSpace > dashTight ? "- " : "-";
        return doc;
    }

    internal static string GapKey(Node prev, Node kind) => (prev, kind) switch
    {
        (Node.Header, _) => nameof(TextLayout.GapAfterHeader),
        (Node.SectionHeader, _) => nameof(TextLayout.GapAfterSection),
        (Node.CategoryHeader or Node.Divider, Node.Entry) => nameof(TextLayout.GapAfterCategory),
        (Node.Entry, Node.Entry) => nameof(TextLayout.GapBetweenEntries),
        _ => nameof(TextLayout.GapBeforeHeading)
    };

    private static int Gap(TextLayout l, Node prev, Node kind) => prev == Node.Start ? 0 : GapKey(prev, kind) switch
    {
        nameof(TextLayout.GapAfterHeader) => l.GapAfterHeader,
        nameof(TextLayout.GapAfterSection) => l.GapAfterSection,
        nameof(TextLayout.GapAfterCategory) => l.GapAfterCategory,
        nameof(TextLayout.GapBetweenEntries) => l.GapBetweenEntries,
        _ => l.GapBeforeHeading
    };

    private static bool IsStructural(string t) =>
        t.StartsWith('-') || TryParseSection(t, out _) || (t.StartsWith('[') && t.EndsWith(']')) || (t.StartsWith('(') && t.EndsWith(')'));

    public static bool TryParseSection(string t, out string name)
    {
        t = t.Trim();
        if (t.Length >= 2 && t.StartsWith('<') && t.EndsWith('>') && !t.Contains("<!--"))
        {
            name = t[1..^1].Trim();
            return true;
        }
        name = "";
        return false;
    }

    public static bool TryParseBlock(string t, out Block block)
    {
        t = t.Trim();
        block = null!;
        if (t.Length >= 2 && t.StartsWith('[') && t.EndsWith(']'))
            block = new Block { Type = BlockType.Divider, Style = BlockStyle.Bracket, Name = t[1..^1].Trim() };
        else if (t.Length >= 2 && t.StartsWith('(') && t.EndsWith(')'))
            block = new Block { Type = BlockType.Category, Style = BlockStyle.Paren, Name = t[1..^1].Trim() };
        else if (t.Length >= 2 && t.EndsWith(':') && !t.StartsWith('-'))
            block = new Block { Type = BlockType.Category, Style = BlockStyle.Colon, Name = t[..^1].Trim() };
        return block != null;
    }

    // ───────────────────────────── escritura ─────────────────────────────

    public static string Write(GameDocument doc)
    {
        var lay = doc.Layout;
        var keepRaw = lay.SourceKind == FileKind.Txt;
        var outLines = new List<string>();
        var prev = Node.Start;

        void Emit(Node kind, string line)
        {
            var g = Gap(lay, prev, kind);
            for (var i = 0; i < g; i++) outLines.Add("");
            outLines.Add(line);
            prev = kind;
        }

        if (doc.HasHeader && doc.Header.Trim().Length > 0) Emit(Node.Header, doc.Header);

        foreach (var s in doc.Sections)
        {
            if (!s.Implicit)
            {
                var raw = keepRaw && s.Raw != null && TryParseSection(s.Raw, out var rn) && rn == s.Name.Trim() ? s.Raw : null;
                Emit(Node.SectionHeader, raw ?? $"<{s.Name.Trim()}>");
            }
            foreach (var b in s.Blocks)
            {
                if (b.Type == BlockType.Divider)
                {
                    Emit(Node.Divider, BlockLine(b, keepRaw));
                    continue;
                }
                if (b.Style != BlockStyle.None) Emit(Node.CategoryHeader, BlockLine(b, keepRaw));
                foreach (var e in b.Entries) Emit(Node.Entry, EntryText(e, lay, keepRaw));
            }
        }

        // Quita líneas en blanco finales y pone las del original.
        while (outLines.Count > 0 && outLines[^1].Length == 0) outLines.RemoveAt(outLines.Count - 1);
        for (var i = 0; i < lay.TrailingBlankLines; i++) outLines.Add("");
        return TextLines.Join(outLines, lay);
    }

    private static string BlockLine(Block b, bool keepRaw)
    {
        if (keepRaw && b.Raw != null && TryParseBlock(b.Raw, out var rb) && rb.Name == b.Name.Trim() && rb.Style == b.Style && rb.Type == b.Type)
            return b.Raw;
        if (keepRaw && b.Raw != null && b.Style == BlockStyle.Plain && b.Raw.Trim() == b.Name.Trim())
            return b.Raw;
        return b.Style switch
        {
            BlockStyle.Bracket => $"[{b.Name.Trim()}]",
            BlockStyle.Paren => $"({b.Name.Trim()})",
            BlockStyle.Plain => b.Name.Trim(),
            _ => $"{b.Name.Trim()}:"
        };
    }

    public static string EntryText(GameEntry e, TextLayout lay, bool keepRaw)
    {
        if (keepRaw && e.Raw != null)
        {
            var t = e.Raw.Trim();
            if (t.StartsWith('-'))
            {
                var parsed = EntryLine.Parse(t[1..]);
                if (EntryLine.Normalized(parsed).SameContent(EntryLine.Normalized(e))) return e.Raw;
            }
        }
        return lay.EntryPrefix + EntryLine.Format(EntryLine.Normalized(e));
    }

    private static readonly Regex UpdatedRx = new(@"\(Actualizado el \d{1,2}/\d{1,2}/\d{2,4}\)", RegexOptions.IgnoreCase);

    /// <summary>Pone la fecha de hoy en «(Actualizado el dd/mm/aaaa)» (o la añade al final).</summary>
    public static string UpdateHeaderDate(string header, DateTime today)
    {
        var stamp = $"(Actualizado el {today:dd/MM/yyyy})";
        if (UpdatedRx.IsMatch(header)) return UpdatedRx.Replace(header, stamp, 1);
        return header.TrimEnd().Length == 0 ? header : header.TrimEnd() + " " + stamp;
    }

    public static (string title, string? updated) SplitHeader(string header)
    {
        var m = Regex.Match(header, @"\s*\(Actualizado el (\d{1,2}/\d{1,2}/\d{2,4})\)\s*", RegexOptions.IgnoreCase);
        if (!m.Success) return (header.Trim(), null);
        return ((header[..m.Index] + " " + header[(m.Index + m.Length)..]).Trim(), m.Groups[1].Value);
    }
}

internal static class TextLines
{
    public static List<string> Split(string text, TextLayout lay)
    {
        var crlf = 0; var lf = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n') { if (i > 0 && text[i - 1] == '\r') crlf++; else lf++; }
        if (crlf + lf > 0) lay.NewLine = crlf >= lf ? "\r\n" : "\n";

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        lay.EndsWithNewLine = text.EndsWith('\n') || text.EndsWith('\r');
        if (lay.EndsWithNewLine && lines.Count > 0) lines.RemoveAt(lines.Count - 1);
        var trailing = 0;
        for (var i = lines.Count - 1; i >= 0 && lines[i].Trim().Length == 0; i--) trailing++;
        lay.TrailingBlankLines = lines.Count == trailing ? 0 : trailing;
        return lines;
    }

    public static string Join(List<string> lines, TextLayout lay)
    {
        var s = string.Join(lay.NewLine, lines);
        return lay.EndsWithNewLine && lines.Count > 0 ? s + lay.NewLine : s;
    }
}
