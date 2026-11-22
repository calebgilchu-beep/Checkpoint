using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ListaJuegos.Core.Connectors;

/// <summary>Juegos de los emuladores instalados en este PC, sin cuentas ni red: listas de RetroArch (.lpl)
/// y las carpetas de juegos configuradas en Dolphin, PCSX2 y DuckStation.</summary>
public static class EmulatorScanner
{
    public const string Note = "emulación";
    internal const int MaxFiles = 20000;

    /// <summary>Busca en todos los emuladores. Cada uno puede recibir sus rutas (para las pruebas) o buscarlas solo.</summary>
    public static ConnectorResult Scan(EmulatorPaths? paths = null)
    {
        paths ??= EmulatorPaths.Default();
        var res = new ConnectorResult { Source = "emuladores" };
        var summary = new List<string>();

        void Run(string name, Func<List<ImportCandidate>?> f)
        {
            List<ImportCandidate>? found;
            try { found = f(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { summary.Add($"{name}: no se pudo leer"); return; }
            if (found == null) return;                       // no instalado: no se menciona
            found = Dedupe(found);                           // varios discos = un juego
            summary.Add($"{name}: {found.Count} {(found.Count == 1 ? "juego" : "juegos")}");
            res.Games.AddRange(found);
        }

        Run("RetroArch", () => RetroArchScanner.Scan(paths.RetroArch));
        Run("Dolphin", () => DolphinScanner.Scan(paths.Dolphin));
        Run("PCSX2", () => DiscFolderScanner.Scan(paths.Pcsx2, "PS2", DiscFolderScanner.Pcsx2Extensions));
        Run("DuckStation", () => DiscFolderScanner.Scan(paths.DuckStation, "PS1", DiscFolderScanner.DuckStationExtensions));

        res.Games = Dedupe(res.Games);
        res.Message = summary.Count == 0
            ? "No se ha encontrado RetroArch, Dolphin, PCSX2 ni DuckStation en este PC."
            : "Emuladores: " + string.Join(" · ", summary) + ".";
        return res;
    }

    /// <summary>Crea el candidato de un juego emulado (título limpio, consola y nota [emulación]).</summary>
    public static ImportCandidate Candidate(string title, string? platform) => new()
    {
        // Fuente por consola: el mismo título en dos consolas son dos juegos distintos (ImportPlanner.Merge).
        Source = "emu-" + (platform ?? "retro"), Title = title, Platform = platform, Note = Note, Installed = true
    };

    /// <summary>Un juego por título y consola (varios discos, la misma ROM en dos listas…), con los datos más completos.</summary>
    public static List<ImportCandidate> Dedupe(IEnumerable<ImportCandidate> games)
    {
        var map = new Dictionary<string, ImportCandidate>();
        foreach (var g in games)
        {
            if (string.IsNullOrWhiteSpace(g.Title)) continue;
            var k = TitleMatch.Key(g.Title) + "|" + (g.Platform ?? "");
            if (!map.TryGetValue(k, out var m)) { map[k] = g; continue; }
            if (g.Hours > (m.Hours ?? 0)) m.Hours = g.Hours;
            if (g.LastPlayed > (m.LastPlayed ?? DateTime.MinValue)) m.LastPlayed = g.LastPlayed;
        }
        return map.Values.ToList();
    }

    internal static IEnumerable<string> Files(string dir, bool recursive, ISet<string> extensions)
    {
        if (!Directory.Exists(dir)) yield break;
        var opts = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, MaxRecursionDepth = 8, AttributesToSkip = FileAttributes.System };
        var n = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", opts))
        {
            if (!extensions.Contains(Path.GetExtension(f))) continue;
            if (++n > MaxFiles) yield break;
            yield return f;
        }
    }
}

/// <summary>Dónde buscar cada emulador. null = buscar en las rutas habituales; lista vacía = no buscar.</summary>
public sealed class EmulatorPaths
{
    public IReadOnlyList<string>? RetroArch { get; init; }     // carpetas de RetroArch (con retroarch.cfg o playlists\)
    public IReadOnlyList<string>? Dolphin { get; init; }       // archivos Dolphin.ini
    public IReadOnlyList<string>? Pcsx2 { get; init; }         // archivos PCSX2.ini
    public IReadOnlyList<string>? DuckStation { get; init; }   // archivos settings.ini

    public static EmulatorPaths Default()
    {
        string Sp(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var appData = Sp(Environment.SpecialFolder.ApplicationData);
        var local = Sp(Environment.SpecialFolder.LocalApplicationData);
        var docs = Sp(Environment.SpecialFolder.MyDocuments);
        var home = Sp(Environment.SpecialFolder.UserProfile);

        var ra = new List<string>
        {
            Path.Combine(appData, "RetroArch"), @"C:\RetroArch-Win64", @"C:\RetroArch", @"C:\RetroArch-Win32",
            Path.Combine(Sp(Environment.SpecialFolder.ProgramFiles), "RetroArch"),
            Path.Combine(home, "scoop", "apps", "retroarch", "current")
        };
        if (SteamLocal.FindSteamPath() is { } steam) ra.Add(Path.Combine(steam, "steamapps", "common", "RetroArch"));

        return new EmulatorPaths
        {
            RetroArch = ra,
            Dolphin = new[]
            {
                Path.Combine(appData, "Dolphin Emulator", "Config", "Dolphin.ini"),
                Path.Combine(docs, "Dolphin Emulator", "Config", "Dolphin.ini")
            },
            Pcsx2 = new[]
            {
                Path.Combine(docs, "PCSX2", "inis", "PCSX2.ini"),
                Path.Combine(appData, "PCSX2", "inis", "PCSX2.ini"),
                Path.Combine(docs, "PCSX2", "inis", "PCSX2_ui.ini")
            },
            DuckStation = new[]
            {
                Path.Combine(local, "DuckStation", "settings.ini"),
                Path.Combine(docs, "DuckStation", "settings.ini")
            }
        };
    }
}

/// <summary>Lector mínimo de .ini: devuelve los pares clave/valor de una sección (las claves pueden repetirse).</summary>
public static class Ini
{
    public static List<(string key, string value)> Section(string path, string section)
    {
        var list = new List<(string, string)>();
        string? current = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line.EndsWith(']')) { current = line[1..^1].Trim(); continue; }
            if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            list.Add((line[..eq].Trim(), Unquote(line[(eq + 1)..].Trim())));
        }
        return list;
    }

    internal static string Unquote(string v) => v.Length >= 2 && v[0] == '"' && v[^1] == '"' ? v[1..^1] : v;

    internal static bool IsTrue(string? v) => v != null && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1");
}

// ───────────────────────────── RetroArch ─────────────────────────────

public static class RetroArchScanner
{
    // Listas que no son de juegos
    private static readonly string[] SkipPlaylists = { "content_music_history", "content_video_history", "content_image_history" };

    /// <summary>null si RetroArch no está en ninguna de las carpetas.</summary>
    public static List<ImportCandidate>? Scan(IEnumerable<string>? roots)
    {
        if (roots == null) return null;
        List<ImportCandidate>? all = null;
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var cfg = ReadConfig(root);
            var playlists = cfg.GetValueOrDefault("playlist_directory") is { } pd ? Resolve(root, pd) : Path.Combine(root, "playlists");
            if (!Directory.Exists(playlists)) continue;
            all ??= new();
            var logs = cfg.GetValueOrDefault("runtime_log_directory") is { } ld ? Resolve(root, ld) : Path.Combine(playlists, "logs");
            var runtimes = ReadRuntimeLogs(logs);
            foreach (var lpl in Directory.EnumerateFiles(playlists, "*.lpl"))
            {
                if (SkipPlaylists.Any(s => Path.GetFileNameWithoutExtension(lpl).Equals(s, StringComparison.OrdinalIgnoreCase))) continue;
                foreach (var g in ReadPlaylist(lpl))
                {
                    if (g.ContentKey != null && runtimes.TryGetValue(g.ContentKey, out var rt))
                    {
                        g.Hours = rt.hours > 0 ? Math.Round(rt.hours, 1) : null;
                        g.LastPlayed = rt.last;
                    }
                    all.Add(g);
                }
            }
        }
        return all;
    }

    private static Dictionary<string, string> ReadConfig(string root)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cfg = Path.Combine(root, "retroarch.cfg");
        if (!File.Exists(cfg)) return map;
        foreach (var raw in File.ReadLines(cfg))
        {
            var eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            var v = Ini.Unquote(raw[(eq + 1)..].Trim());
            if (v.Length > 0 && !v.Equals("default", StringComparison.OrdinalIgnoreCase)) map[raw[..eq].Trim()] = v;
        }
        return map;
    }

    /// <summary>«:\playlists» es relativo a la carpeta de RetroArch.</summary>
    private static string Resolve(string root, string value) =>
        value.StartsWith(':') ? Path.Combine(root, value.TrimStart(':').TrimStart('\\', '/')) : value;

    /// <summary>Lee una lista .lpl: formato JSON (RetroArch 1.7.6+) o el antiguo de 6 líneas por juego.
    /// En <see cref="ImportCandidate.ContentKey"/> deja el nombre del contenido para emparejar el tiempo de juego.</summary>
    public static List<ImportCandidate> ReadPlaylist(string path)
    {
        var text = File.ReadAllText(path);
        var system = Path.GetFileNameWithoutExtension(path);
        var items = new List<(string path, string? label, string? corePath, string? coreName, string? db)>();

        if (text.TrimStart().StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var defCore = S(root, "default_core_path");
            if (root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var it in arr.EnumerateArray())
                    if (S(it, "path") is { Length: > 0 } p)
                        items.Add((p, S(it, "label"), S(it, "core_path") is { } cp && cp != "DETECT" ? cp : defCore, S(it, "core_name"), S(it, "db_name")));
        }
        else
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i + 1 < lines.Length; i += 6)
            {
                string? L(int k) => i + k < lines.Length && lines[i + k].Trim() is { Length: > 0 } v ? v : null;
                if (L(0) is { } p) items.Add((p, L(1), L(2), L(3), L(5)));
            }
        }

        var list = new List<ImportCandidate>();
        foreach (var (p, label, corePath, coreName, db) in items)
        {
            var title = !string.IsNullOrWhiteSpace(label) ? RomName.Clean(label) : RomName.FromFile(p);
            if (title.Length == 0) continue;
            var core = corePath is { } c && c != "DETECT" ? Path.GetFileNameWithoutExtension(c).Replace("_libretro", "") : null;
            var platform = RetroPlatforms.Find(db?.Replace(".lpl", ""), system.StartsWith("content_") ? null : system,
                                               coreName is "DETECT" ? null : coreName, core, Path.GetExtension(StripArchive(p)));
            var g = EmulatorScanner.Candidate(title, platform);
            g.ContentKey = ContentName(p);
            list.Add(g);
        }
        return list;
    }

    /// <summary>Tiempo de juego por contenido: registros .lrtl de RetroArch (playlists\logs\&lt;núcleo&gt;\&lt;juego&gt;.lrtl).</summary>
    public static Dictionary<string, (double hours, DateTime? last)> ReadRuntimeLogs(string dir)
    {
        var map = new Dictionary<string, (double hours, DateTime? last)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir)) return map;
        foreach (var f in Directory.EnumerateFiles(dir, "*.lrtl", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 3 }))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var hours = ParseRuntime(S(doc.RootElement, "runtime"));
                DateTime? last = DateTime.TryParseExact(S(doc.RootElement, "last_played"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
                var key = Path.GetFileNameWithoutExtension(f);
                var prev = map.GetValueOrDefault(key);
                map[key] = (prev.hours + hours, last > prev.last || prev.last == null ? last ?? prev.last : prev.last);
            }
            catch (Exception e) when (e is IOException or JsonException) { }
        }
        return map;
    }

    /// <summary>«12:34:56» (horas sin tope) → horas.</summary>
    public static double ParseRuntime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var p = s.Split(':');
        if (p.Length != 3 || !int.TryParse(p[0], out var h) || !int.TryParse(p[1], out var m) || !int.TryParse(p[2], out var sec)) return 0;
        return h + m / 60.0 + sec / 3600.0;
    }

    private static string StripArchive(string p) => p.Contains('#') ? p[(p.IndexOf('#') + 1)..] : p;

    /// <summary>Nombre del contenido como lo usa RetroArch para los registros: archivo sin carpeta ni extensión.</summary>
    private static string ContentName(string p)
    {
        var file = p.Contains('#') ? p[..p.IndexOf('#')] : p;
        return Path.GetFileNameWithoutExtension(file.Replace('/', '\\').Split('\\')[^1]);
    }

    private static string? S(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

// ───────────────────────────── Dolphin (GameCube y Wii) ─────────────────────────────

public static class DolphinScanner
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".iso", ".gcm", ".ciso", ".gcz", ".wbfs", ".rvz", ".wia", ".wad", ".tgc" };

    /// <summary>null si no hay ningún Dolphin.ini.</summary>
    public static List<ImportCandidate>? Scan(IEnumerable<string>? configs)
    {
        if (configs == null) return null;
        List<ImportCandidate>? all = null;
        foreach (var ini in configs.Where(File.Exists))
        {
            all ??= new();
            var general = Ini.Section(ini, "General");
            var recursive = Ini.IsTrue(general.FirstOrDefault(k => k.key.Equals("RecursiveISOPaths", StringComparison.OrdinalIgnoreCase)).value);
            var dirs = general.Where(k => k.key.StartsWith("ISOPath", StringComparison.OrdinalIgnoreCase) && !k.key.Equals("ISOPaths", StringComparison.OrdinalIgnoreCase))
                              .Select(k => k.value).Where(v => v.Length > 0);
            foreach (var dir in dirs)
                foreach (var f in EmulatorScanner.Files(dir, recursive, Extensions))
                    all.Add(EmulatorScanner.Candidate(RomName.FromFile(f), DetectPlatform(f)));
        }
        return all;
    }

    /// <summary>«GameCube» o «Wii» leyendo la cabecera del disco (o del contenedor .gcz/.rvz/.wia).</summary>
    public static string DetectPlatform(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".wbfs" or ".wad") return "Wii";
        if (ext is ".ciso" or ".tgc") return "GameCube";
        try
        {
            using var fs = File.OpenRead(path);
            var h = new byte[0x80];
            var n = fs.Read(h, 0, h.Length);
            if (ext == ".gcz" && n >= 8) return BitConverter.ToUInt32(h, 4) == 1 ? "Wii" : "GameCube";       // sub_type: 0 GC, 1 Wii
            if (ext is ".rvz" or ".wia" && n >= 0x4C) return BigEndian(h, 0x48) == 2 ? "Wii" : "GameCube";  // disc_type: 1 GC, 2 Wii
            if (n >= 0x20)
            {
                if (BigEndian(h, 0x18) == 0x5D1C9EA3) return "Wii";
                if (BigEndian(h, 0x1C) == 0xC2339F3D) return "GameCube";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return "GameCube";
    }

    private static uint BigEndian(byte[] b, int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);
}

// ───────────────────────────── PCSX2 (PS2) y DuckStation (PS1) ─────────────────────────────

/// <summary>Emuladores que guardan sus carpetas de juegos en [GameList] Paths / RecursivePaths.</summary>
public static class DiscFolderScanner
{
    public static readonly HashSet<string> Pcsx2Extensions = new(StringComparer.OrdinalIgnoreCase) { ".iso", ".chd", ".cso", ".zso", ".gz", ".mdf", ".nrg" };
    public static readonly HashSet<string> DuckStationExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".cue", ".chd", ".pbp", ".m3u", ".ecm", ".mds", ".ccd", ".iso", ".img", ".bin" };

    /// <summary>null si no hay ningún archivo de configuración.</summary>
    public static List<ImportCandidate>? Scan(IEnumerable<string>? configs, string platform, ISet<string> extensions)
    {
        if (configs == null) return null;
        List<ImportCandidate>? all = null;
        foreach (var ini in configs.Where(File.Exists))
        {
            all ??= new();
            var dirs = Ini.Section(ini, "GameList")
                .Where(k => k.key.Equals("Paths", StringComparison.OrdinalIgnoreCase) || k.key.Equals("RecursivePaths", StringComparison.OrdinalIgnoreCase))
                .Select(k => (dir: k.value, recursive: k.key.StartsWith("Recursive", StringComparison.OrdinalIgnoreCase)))
                .Where(d => d.dir.Length > 0);
            foreach (var (dir, recursive) in dirs)
                all.AddRange(GamesIn(EmulatorScanner.Files(dir, recursive, extensions).ToList(), platform));
        }
        return all;
    }

    /// <summary>Quita las pistas sueltas: los .bin que tienen su .cue y los discos que ya cuenta un .m3u.</summary>
    public static IEnumerable<ImportCandidate> GamesIn(List<string> files, string platform)
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m3u in files.Where(f => f.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                foreach (var line in File.ReadLines(m3u, Encoding.UTF8))
                    if (line.Trim() is { Length: > 0 } l && !l.StartsWith('#'))
                        skip.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(m3u)!, l)));
            }
            catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException) { }
        }
        var dirsWithCue = files.Where(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
                               .Select(Path.GetDirectoryName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            if (skip.Contains(Path.GetFullPath(f))) continue;
            if (f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) && dirsWithCue.Contains(Path.GetDirectoryName(f))) continue;
            yield return EmulatorScanner.Candidate(RomName.FromFile(f), platform);
        }
    }
}
