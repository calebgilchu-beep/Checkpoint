using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ListaJuegos.Core.Connectors;

/// <summary>Steam: Web API (clave + SteamID), tienda pública (portadas, géneros) y biblioteca local.</summary>
public sealed class SteamClient(HttpClient http)
{
    private const string Api = "https://api.steampowered.com";
    private const string Store = "https://store.steampowered.com";

    public static string CoverUrl(string appId) => $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900.jpg";
    public static string HeaderUrl(string appId) => $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg";

    /// <summary>Acepta SteamID64, nombre de URL personalizada o el enlace del perfil.</summary>
    public async Task<string> ResolveSteamIdAsync(string key, string input, CancellationToken ct = default)
    {
        input = input.Trim().TrimEnd('/');
        var m = Regex.Match(input, @"(?:profiles/)?(\d{17})$");
        if (m.Success) return m.Groups[1].Value;
        var vm = Regex.Match(input, @"(?:/id/)?([A-Za-z0-9_-]{2,32})$");
        if (!vm.Success) throw new ConnectorException("El SteamID no es válido. Usa tu SteamID64 (17 cifras) o el enlace de tu perfil.");
        using var doc = await GetJson($"{Api}/ISteamUser/ResolveVanityURL/v1/?key={Uri.EscapeDataString(key)}&vanityurl={Uri.EscapeDataString(vm.Groups[1].Value)}", ct);
        var r = doc.RootElement.GetProperty("response");
        if (r.TryGetProperty("success", out var ok) && ok.GetInt32() == 1) return r.GetProperty("steamid").GetString()!;
        throw new ConnectorException("No se encontró ese perfil de Steam.");
    }

    public async Task<ConnectorResult> GetLibraryAsync(string key, string steamIdInput, bool achievements, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var res = new ConnectorResult { Source = "steam" };
        var steamId = await ResolveSteamIdAsync(key, steamIdInput, ct);
        progress?.Report("Leyendo tu biblioteca de Steam…");
        using var owned = await GetJson($"{Api}/IPlayerService/GetOwnedGames/v1/?key={Uri.EscapeDataString(key)}&steamid={steamId}&include_appinfo=1&include_played_free_games=1", ct);
        var resp = owned.RootElement.GetProperty("response");
        if (!resp.TryGetProperty("games", out var games))
            throw new ConnectorException("Steam no ha devuelto juegos. Comprueba que «Detalles del juego» de tu perfil es público.");

        foreach (var g in games.EnumerateArray())
        {
            var id = g.GetProperty("appid").GetInt64().ToString();
            var minutes = g.TryGetProperty("playtime_forever", out var p) ? p.GetDouble() : 0;
            DateTime? last = g.TryGetProperty("rtime_last_played", out var lp) && lp.GetInt64() > 0
                ? DateTimeOffset.FromUnixTimeSeconds(lp.GetInt64()).LocalDateTime : null;
            res.Games.Add(new ImportCandidate
            {
                Source = "steam", StoreKey = "steam", StoreId = id,
                Title = g.TryGetProperty("name", out var n) ? n.GetString() ?? id : id,
                Hours = Math.Round(minutes / 60.0, 1), LastPlayed = last, Platform = "Steam",
                CoverUrl = CoverUrl(id)
            });
        }

        if (achievements)
        {
            var played = res.Games.Where(g => g.Hours > 0).ToList();
            var done = 0;
            using var gate = new SemaphoreSlim(4);
            await Task.WhenAll(played.Select(async g =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    g.Achievements = await GetAchievementPercentAsync(key, steamId, g.StoreId!, ct);
                    var d = Interlocked.Increment(ref done);
                    if (d % 10 == 0) progress?.Report($"Logros: {d}/{played.Count}");
                }
                catch (Exception e) when (e is HttpRequestException or ConnectorException or JsonException) { }
                finally { gate.Release(); }
            }));
        }
        return res;
    }

    public async Task<double?> GetAchievementPercentAsync(string key, string steamId, string appId, CancellationToken ct = default)
    {
        using var r = await http.GetAsync($"{Api}/ISteamUserStats/GetPlayerAchievements/v1/?key={Uri.EscapeDataString(key)}&steamid={steamId}&appid={appId}", ct);
        if (!r.IsSuccessStatusCode) return null; // juegos sin logros devuelven 400
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.GetProperty("playerstats").TryGetProperty("achievements", out var a)) return null;
        var total = 0; var got = 0;
        foreach (var x in a.EnumerateArray()) { total++; if (x.GetProperty("achieved").GetInt32() == 1) got++; }
        return total == 0 ? null : Math.Round(100.0 * got / total, 0);
    }

    /// <summary>Busca un juego en la tienda (sin clave). Devuelve el appid si el nombre coincide de verdad.</summary>
    public async Task<(string appId, string name)?> FindAppAsync(string title, CancellationToken ct = default)
    {
        var q = TitleMatch.SearchQuery(title);
        if (q.Length < 2) return null;
        using var doc = await GetJson($"{Store}/api/storesearch/?term={Uri.EscapeDataString(q)}&l=spanish&cc=ES", ct);
        if (!doc.RootElement.TryGetProperty("items", out var items)) return null;
        foreach (var it in items.EnumerateArray())
        {
            var name = it.GetProperty("name").GetString() ?? "";
            if (TitleMatch.IsSameGame(q, name)) return (it.GetProperty("id").GetInt64().ToString(), name);
        }
        return null;
    }

    public async Task<List<string>> GetGenresAsync(string appId, CancellationToken ct = default)
    {
        using var doc = await GetJson($"{Store}/api/appdetails?appids={appId}&l=spanish&filters=genres", ct);
        var list = new List<string>();
        if (doc.RootElement.TryGetProperty(appId, out var a) && a.TryGetProperty("data", out var d) && d.TryGetProperty("genres", out var g))
            foreach (var x in g.EnumerateArray()) list.Add(x.GetProperty("description").GetString() ?? "");
        return list;
    }

    private async Task<JsonDocument> GetJson(string url, CancellationToken ct)
    {
        using var r = await http.GetAsync(url, ct);
        if (r.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
            throw new ConnectorException("Steam ha rechazado la clave de la Web API. Revísala en Ajustes.");
        if (!r.IsSuccessStatusCode) throw new ConnectorException($"Steam respondió con un error ({(int)r.StatusCode}).");
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
    }
}

public static class SteamLocal
{
    public static string? FindSteamPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var c in new[] { ".steam/steam", ".local/share/Steam", ".var/app/com.valvesoftware.Steam/.local/share/Steam", "snap/steam/common/.local/share/Steam" })
            {
                var d = Path.Combine(home, c);
                if (Directory.Exists(Path.Combine(d, "steamapps"))) return d;
            }
            return null;
        }
        var p = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        return p != null && Directory.Exists(p) ? p.Replace('/', '\\') : null;
    }

    public static ConnectorResult Scan(string? steamPath = null)
    {
        var res = new ConnectorResult { Source = "steam" };
        steamPath ??= FindSteamPath();
        if (steamPath == null) { res.Ok = false; res.Message = "Steam no está instalado en este PC."; return res; }

        var libs = new List<string> { steamPath };
        var lf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(lf))
        {
            var root = Vdf.Parse(File.ReadAllText(lf)).Child("libraryfolders");
            if (root != null)
                foreach (var v in root.Values.OfType<Vdf.Node>())
                    if (v.Str("path") is { } path) libs.Add(path.Replace(@"\\", @"\"));
        }

        // Horas y última partida de la configuración local de cada usuario.
        var playtime = new Dictionary<string, (double min, long last)>();
        var userdata = Path.Combine(steamPath, "userdata");
        if (Directory.Exists(userdata))
            foreach (var cfg in Directory.EnumerateFiles(userdata, "localconfig.vdf", SearchOption.AllDirectories))
            {
                try
                {
                    var apps = Vdf.Parse(File.ReadAllText(cfg)).Child("UserLocalConfigStore")?.Child("Software")?.Child("Valve")?.Child("Steam")?.Child("apps");
                    if (apps == null) continue;
                    foreach (var (id, node) in apps)
                        if (node is Vdf.Node n)
                        {
                            double.TryParse(n.Str("Playtime"), out var min);
                            long.TryParse(n.Str("LastPlayed"), out var last);
                            if (!playtime.TryGetValue(id, out var cur) || min > cur.min) playtime[id] = (min, last);
                        }
                }
                catch (IOException) { }
            }

        var seen = new HashSet<string>();
        foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var st = Vdf.Parse(File.ReadAllText(acf)).Child("AppState");
                    var id = st?.Str("appid"); var name = st?.Str("name");
                    if (id == null || name == null || !seen.Add(id)) continue;
                    if (Regex.IsMatch(name, @"(Redistributable|Steamworks Common|Proton|Steam Linux Runtime|SteamVR)", RegexOptions.IgnoreCase)) continue;
                    var c = new ImportCandidate
                    {
                        Source = "steam", StoreKey = "steam", StoreId = id, Title = name, Installed = true,
                        Platform = "Steam", CoverUrl = SteamClient.CoverUrl(id)
                    };
                    if (playtime.TryGetValue(id, out var pt))
                    {
                        c.Hours = Math.Round(pt.min / 60.0, 1);
                        if (pt.last > 0) c.LastPlayed = DateTimeOffset.FromUnixTimeSeconds(pt.last).LocalDateTime;
                    }
                    res.Games.Add(c);
                }
                catch (IOException) { }
            }
        }
        return res;
    }
}

public sealed class ConnectorException(string message) : Exception(message);
