using System.Text;
using System.Text.RegularExpressions;

namespace ListaJuegos.Core.Connectors;

public static partial class TitleMatch
{
    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]|\{[^}]*\}|#[^#]*#")] private static partial Regex BracketsRx();
    [GeneratedRegex(@"[™®©]")] private static partial Regex MarksRx();
    [GeneratedRegex(@"\+\s*DLC.*$", RegexOptions.IgnoreCase)] private static partial Regex DlcRx();
    [GeneratedRegex(@"[^\p{L}\p{N}]+")] private static partial Regex NonWordRx();

    private static readonly Dictionary<string, string> Roman = new()
    {
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10"
    };

    /// <summary>Clave para comparar títulos: sin acentos, ni símbolos, ni (OG)/(Remake), ni + DLC.</summary>
    public static string Key(string title)
    {
        var s = MarksRx().Replace(title, "");
        s = DlcRx().Replace(s, "");
        s = BracketsRx().Replace(s, " ");
        s = SectionKinds.Normalize(s);
        s = s.Replace("&", " and ");
        var words = NonWordRx().Split(s).Where(w => w.Length > 0).Select(w => Roman.TryGetValue(w, out var r) ? r : w);
        return string.Join(' ', words);
    }

    /// <summary>Texto para buscar en una tienda.</summary>
    public static string SearchQuery(string title)
    {
        var s = MarksRx().Replace(title, "");
        s = DlcRx().Replace(s, "");
        s = BracketsRx().Replace(s, " ");
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }

    public static bool IsSameGame(string a, string b)
    {
        var ka = Key(a); var kb = Key(b);
        if (ka.Length == 0 || kb.Length == 0) return false;
        if (ka == kb) return true;
        // «Death Stranding» ~ «Death Stranding Director's Cut» o «… PC/Steam», pero no «Mario» ~ «Mario Kart»
        // ni «Hollow Knight» ~ «Hollow Knight: Silksong».
        var (shortK, longK) = ka.Length <= kb.Length ? (ka, kb) : (kb, ka);
        if (!longK.StartsWith(shortK + " ") || shortK.Split(' ').Length < 2) return false;
        return longK[shortK.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries).All(ExtraWords.Contains);
    }

    private static readonly HashSet<string> ExtraWords = new()
    {
        "edition", "edicion", "remastered", "remaster", "definitive", "deluxe", "goty", "game", "of", "the", "year", "director", "directors", "s", "cut",
        "complete", "enhanced", "hd", "remake", "classic", "anniversary", "ultimate", "standard", "digital", "collection",
        "pc", "steam", "switch", "nintendo", "ps", "ps4", "ps5", "vita", "xbox", "one", "series", "windows", "gog", "epic", "og"
    };
}
