using System.Globalization;
using System.Text;

namespace ListaJuegos.Core;

public static class SectionKinds
{
    public const string DefaultFirstSection = "Completados";

    /// <summary>Secciones que la app propone además de las del archivo.</summary>
    public static readonly string[] Suggested =
        { "Completados", "Completados al 100%", "Jugando ahora", "Inconclusos", "Por jugar", "Abandonados", "Lista de deseos" };

    public static SectionKind Infer(string name, bool isImplicit = false)
    {
        if (isImplicit) return SectionKind.Completed;
        var n = Normalize(name);
        if (n.Contains("100") || n.Contains("platin")) return SectionKind.Hundred;
        if (n.Contains("jugando") || n.Contains("en curso") || n.Contains("playing")) return SectionKind.Playing;
        if (n.Contains("nclus") || n.Contains("sin terminar") || n.Contains("a medias")) return SectionKind.Unfinished;
        if (n.Contains("abandon") || n.Contains("dropped")) return SectionKind.Abandoned;
        if (n.Contains("deseo") || n.Contains("wish") || n.Contains("quiero")) return SectionKind.Wishlist;
        if (n.Contains("por jugar") || n.Contains("pendiente") || n.Contains("backlog")) return SectionKind.Backlog;
        if (n.Contains("pasad") || n.Contains("complet") || n.Contains("terminad") || n.Contains("acabad")) return SectionKind.Completed;
        return SectionKind.Custom;
    }

    public static string Emoji(SectionKind k) => k switch
    {
        SectionKind.Completed => "🏆",
        SectionKind.Hundred => "💯",
        SectionKind.Playing => "🎮",
        SectionKind.Unfinished => "⏸️",
        SectionKind.Backlog => "📋",
        SectionKind.Abandoned => "🚫",
        SectionKind.Wishlist => "⭐",
        _ => "📁"
    };

    public static bool IsDone(SectionKind k) => k is SectionKind.Completed or SectionKind.Hundred;

    public static string Normalize(string s)
    {
        var d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Quita emojis y símbolos iniciales («🏆 Completados» → «Completados»).</summary>
    public static string StripLeadingSymbols(string s)
    {
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (char.IsHighSurrogate(c) || char.IsLowSurrogate(c) || cat is UnicodeCategory.OtherSymbol or UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.EnclosingMark or UnicodeCategory.Format || c == '️' || c == '‍')
            { i++; continue; }
            break;
        }
        return i == 0 ? s : s[i..].TrimStart();
    }
}
