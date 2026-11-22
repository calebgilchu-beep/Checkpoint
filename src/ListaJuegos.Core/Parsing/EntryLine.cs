using System.Text;
using System.Text.RegularExpressions;

namespace ListaJuegos.Core.Parsing;

/// <summary>Lee y escribe el cuerpo de una línea de juego del .txt:
/// «Título [nota] - Plataformas [nota] #fecha# {campo} {campo}».</summary>
public static partial class EntryLine
{
    [GeneratedRegex(@"\{([^{}]*)\}")] private static partial Regex FieldRx();
    [GeneratedRegex(@"#([^#]+)#")] private static partial Regex DateRx();
    [GeneratedRegex(@"\[([^\[\]]*)\]")] private static partial Regex NoteRx();
    [GeneratedRegex(@"\s+[-–—]\s+")] private static partial Regex SepRx();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex MultiSpaceRx();

    public static GameEntry Parse(string body)
    {
        var e = new GameEntry();
        var s = body;

        foreach (Match m in FieldRx().Matches(s)) e.Fields.Add(m.Groups[1].Value.Trim());
        s = FieldRx().Replace(s, "");

        var dm = DateRx().Match(s);
        if (dm.Success)
        {
            e.Date = dm.Groups[1].Value.Trim();
            s = s.Remove(dm.Index, dm.Length);
        }

        var notes = NoteRx().Matches(s);
        int noteIndex = -1;
        if (notes.Count == 1)
        {
            e.Note = notes[0].Groups[1].Value.Trim();
            noteIndex = notes[0].Index;
            s = s.Remove(notes[0].Index, notes[0].Length);
        }

        var seps = SepRx().Matches(s);
        if (seps.Count > 0)
        {
            var last = seps[^1];
            e.Title = Clean(s[..last.Index]);
            var plat = Clean(s[(last.Index + last.Length)..]);
            e.Platforms = plat.Length > 0 ? plat : null;
            if (e.Note != null)
                e.NotePos = noteIndex < last.Index + last.Length ?NotePosition.BeforePlatform : NotePosition.AfterPlatform;
        }
        else
        {
            e.Title = Clean(s);
            e.NotePos = NotePosition.AfterPlatform;
        }
        return e;
    }

    public static string Format(GameEntry e)
    {
        var sb = new StringBuilder(e.Title.Trim());
        var hasNote = !string.IsNullOrWhiteSpace(e.Note);
        var hasPlat = !string.IsNullOrWhiteSpace(e.Platforms);
        if (hasNote && hasPlat && e.NotePos == NotePosition.BeforePlatform) sb.Append(" [").Append(e.Note!.Trim()).Append(']');
        if (hasPlat) sb.Append(" - ").Append(e.Platforms!.Trim());
        if (hasNote && !(hasPlat && e.NotePos == NotePosition.BeforePlatform)) sb.Append(" [").Append(e.Note!.Trim()).Append(']');
        if (!string.IsNullOrWhiteSpace(e.Date)) sb.Append(" #").Append(e.Date!.Trim()).Append('#');
        foreach (var f in e.Fields)
            if (!string.IsNullOrWhiteSpace(f)) sb.Append(" {").Append(f.Trim()).Append('}');
        return sb.ToString();
    }

    /// <summary>Copia normalizada para comparar con el contenido leído de una línea original.</summary>
    public static GameEntry Normalized(GameEntry e)
    {
        var c = e.Clone();
        c.Title = Clean(c.Title);
        c.Platforms = string.IsNullOrWhiteSpace(c.Platforms) ? null : Clean(c.Platforms);
        c.Note = string.IsNullOrWhiteSpace(c.Note) ? null : c.Note.Trim();
        c.Date = string.IsNullOrWhiteSpace(c.Date) ? null : c.Date.Trim();
        c.Fields = c.Fields.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToList();
        if (c.Platforms == null && c.Note != null) c.NotePos = NotePosition.AfterPlatform;
        return c;
    }

    private static string Clean(string s) => MultiSpaceRx().Replace(s, " ").Trim();
}
