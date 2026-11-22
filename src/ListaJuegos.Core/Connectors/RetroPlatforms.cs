using System.Text.RegularExpressions;

namespace ListaJuegos.Core.Connectors;

/// <summary>Consolas retro: nombre corto para la lista («SNES», «PS1», «GBA»…) a partir del nombre que usan
/// RetroAchievements, las listas de RetroArch («Nintendo - Super Nintendo Entertainment System») o los núcleos.</summary>
public static partial class RetroPlatforms
{
    // De lo más concreto a lo más general: «Game Boy Advance» antes que «Game Boy», «SNES» antes que «NES»,
    // «PlayStation 2» antes que «PlayStation». Se busca con límites de palabra («genesis» no contiene «nes»).
    private static readonly (string name, Regex rx)[] Table = Build(
        ("GBA", @"game ?boy advance|\bgba\b|\bmgba\b|\bgpsp\b|\bvba(?:m|-m|_next)?\b|\bvbam\b|meteor"),
        ("GBC", @"game ?boy colou?r|\bgbc\b"),
        ("GB", @"game ?boy|\bgb\b|gambatte|sameboy|gearboy|\btgbdual\b"),
        ("Virtual Boy", @"virtual ?boy"),
        ("SNES", @"super nintendo|super famicom|\bsnes\b|\bsfc\b|snes9x|\bbsnes|\bmesen-s\b"),
        ("Mega Drive", @"mega ?drive|genesis|\bmd\b|picodrive|blastem"),
        ("Sega CD", @"mega-?cd|sega ?cd"),
        ("32X", @"\b32x\b"),
        ("Master System", @"master ?system|mark iii|\bsms\b"),
        ("Game Gear", @"game ?gear"),
        ("Saturn", @"saturn|yabause|kronos"),
        ("Dreamcast", @"dreamcast|flycast|redream|\bnaomi\b"),
        ("NES", @"nintendo entertainment system|\bnes\b|famicom|family computer|disk system|nestopia|fceumm|\bmesen\b|quicknes"),
        ("N64", @"nintendo 64|\bn64\b|mupen64|parallel[ _]?n64"),
        ("3DS", @"\b3ds\b|citra|lime3ds|azahar"),
        ("NDS", @"nintendo dsi?\b|\bnds\b|\bdsi?\b|melonds|desmume"),
        ("GameCube", @"gamecube|game cube|\bngc\b|\bgcn?\b"),
        ("Wii U", @"wii ?u\b|cemu"),
        ("Wii", @"\bwii\b"),
        ("Switch", @"\bswitch\b|yuzu|ryujinx|suyu|sudachi|citron"),
        ("PS Vita", @"playstation vita|\bps ?vita\b|vita3k"),
        ("PSP", @"playstation portable|\bpsp\b|ppsspp"),
        ("PS3", @"playstation 3|\bps3\b|rpcs3"),
        ("PS2", @"playstation 2|\bps2\b|pcsx2|\blrps2\b|\bplay!"),
        ("PS1", @"playstation|\bpsx\b|\bps1\b|\bpsone\b|pcsx[_ -]?rearmed|swanstation|duckstation|beetle psx|mednafen[_ ]psx"),
        ("PC Engine", @"pc ?engine|turbo ?grafx|\bpce\b"),
        ("Neo Geo Pocket", @"neo ?geo pocket|\bngp\b"),
        ("Neo Geo", @"neo ?geo"),
        ("Atari 2600", @"atari 2600|stella"),
        ("Atari 7800", @"atari 7800|prosystem"),
        ("Lynx", @"\blynx\b|handy"),
        ("Jaguar", @"jaguar|virtualjaguar"),
        ("WonderSwan", @"wonder ?swan"),
        ("MSX", @"\bmsx2?\b|bluemsx|fmsx"),
        ("DOS", @"\bdos\b|dosbox"),
        ("Arcade", @"arcade|\bmame\b|fbneo|fb ?alpha|finalburn|\bcps[123]\b"));

    private static (string, Regex)[] Build(params (string name, string pattern)[] rows) =>
        rows.Select(r => (r.name, new Regex(r.pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))).ToArray();

    /// <summary>Nombre corto de la consola, o null si no se reconoce.</summary>
    public static string? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Replace('_', ' ');
        foreach (var (name, rx) in Table)
            if (rx.IsMatch(t)) return name;
        return null;
    }

    /// <summary>Consola del primero de los textos que se reconozca (sistema, núcleo, ruta…).</summary>
    public static string? Find(params string?[] texts)
    {
        foreach (var t in texts)
            if (Find(t) is { } p) return p;
        return null;
    }
}

/// <summary>Limpia nombres de ROM e ISO («Legend of Zelda, The - A Link to the Past (USA) (Rev 1) [!].sfc»)
/// para emparejarlos con la lista, el diccionario de sagas y SteamGridDB.</summary>
public static partial class RomName
{
    [GeneratedRegex(@"\s*(\([^()]*\)|\[[^\[\]]*\])\s*$")] private static partial Regex TrailingTagRx();
    [GeneratedRegex(@"^~[^~]+~\s*")] private static partial Regex RaPrefixRx();          // «~Hack~ …» de RetroAchievements
    [GeneratedRegex(@"^(.+?),\s*(The|A|An|El|La|Los|Las|Le|Les|Der|Die|Das|Il|Lo|Gli|I)\b(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ArticleRx();
    [GeneratedRegex(@"\s+[-–—]\s+|\s*:\s+")] private static partial Regex DashRx();     // « - » o «: » entre título y subtítulo
    [GeneratedRegex(@"\s{2,}")] private static partial Regex SpacesRx();

    private static readonly HashSet<string> RomExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".nes", ".fds", ".unf", ".sfc", ".smc", ".fig", ".swc", ".n64", ".z64", ".v64", ".gb", ".gbc", ".gba", ".nds", ".dsi",
        ".3ds", ".cia", ".cci", ".vb", ".md", ".gen", ".smd", ".bin", ".sms", ".gg", ".32x", ".pce", ".sgx", ".ngp", ".ngc", ".ws", ".wsc", ".a26",
        ".a78", ".lnx", ".j64", ".iso", ".cue", ".chd", ".pbp", ".m3u", ".ccd", ".mds", ".ecm", ".img", ".cso", ".zso", ".gcm", ".gcz", ".ciso",
        ".rvz", ".wia", ".wbfs", ".wad", ".dol", ".elf", ".nsp", ".xci", ".gdi", ".cdi", ".rom", ".mx1", ".mx2", ".dsk", ".lha"
    };

    /// <summary>Título limpio a partir de un nombre de archivo de ROM (quita carpeta y extensión).</summary>
    public static string FromFile(string path)
    {
        var name = Path.GetFileName(path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        if (name.Contains('#')) name = name[..name.IndexOf('#')];   // «juego.zip#juego.sfc» de RetroArch
        var ext = Path.GetExtension(name);
        if (RomExtensions.Contains(ext)) name = name[..^ext.Length];
        return Clean(name);
    }

    /// <summary>Quita las etiquetas finales de región, idioma, revisión, disco o volcado — «(USA)», «(Rev 1)»,
    /// «(En,Fr,De)», «(Disc 1)», «[!]», «[b1]» — y deja el artículo delante («Legend of Zelda, The» → «The Legend of Zelda»).
    /// Los separadores « - » pasan a «: » porque en el .txt « - » separa el título de la plataforma.</summary>
    public static string Clean(string name)
    {
        var s = name.Replace('_', ' ').Trim();
        s = RaPrefixRx().Replace(s, "");
        string prev;
        do { prev = s; s = TrailingTagRx().Replace(s, ""); } while (s != prev && s.Length > 0);
        if (s.Length == 0) s = name.Trim();

        // «Legend of Zelda, The - A Link to the Past» → «The Legend of Zelda - A Link to the Past»
        var parts = DashRx().Split(s);
        for (var i = 0; i < parts.Length; i++)
        {
            var m = ArticleRx().Match(parts[i]);
            if (m.Success && m.Groups[3].Value.Trim().Length == 0)
            {
                var art = m.Groups[2].Value;
                parts[i] = art.EndsWith('\'') ? art + m.Groups[1].Value.Trim() : art + " " + m.Groups[1].Value.Trim();
            }
        }
        s = string.Join(": ", parts.Select(p => p.Trim()).Where(p => p.Length > 0));
        s = s.Replace(" :", ":");
        return SpacesRx().Replace(s, " ").Trim();
    }
}
