using System.Net;
using System.Text;
using System.Text.Json;

namespace ListaJuegos.Core.Connectors;

/// <summary>Un juego de IGDB con su duración media (horas) según los jugadores.</summary>
public sealed record IgdbDuration
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string? Slug { get; init; }
    public int? Year { get; init; }
    public List<string> Platforms { get; init; } = new();
    /// <summary>Historia principal («normally»; si falta, «hastily»), en horas redondeadas a media hora.</summary>
    public double? Main { get; init; }
    /// <summary>Al 100 % («completely»), en horas.</summary>
    public double? Full { get; init; }
    /// <summary>Cuántas personas han enviado su tiempo.</summary>
    public int Count { get; init; }
    /// <summary>¿El título coincide con el buscado (sin mayúsculas, tildes, signos ni etiquetas como «(Remake)»)?</summary>
    public bool Exact { get; init; }
    public string Url => Slug is { Length: > 0 } s ? $"https://www.igdb.com/games/{s}" : $"https://www.igdb.com/search?q={Uri.EscapeDataString(Name)}";
    /// <summary>El campo {ttb: …} que se escribiría, o null si no hay tiempo de historia.</summary>
    public string? Field => Main is { } m ? Fields.MakeDuration(m, Full is { } f && f > m ? f : null) : null;
}

/// <summary>
/// Duración de los juegos con la API oficial de IGDB (de Twitch). Necesita el Client ID y el secreto de una aplicación
/// gratuita de dev.twitch.tv. Solo se envían títulos de juegos y solo cuando la persona lo pide.
/// HowLongToBeat no tiene API y prohíbe la consulta automática: para ella solo hay un enlace (<see cref="HltbSearchUrl"/>).
/// </summary>
public sealed class IgdbClient(HttpClient http, string clientId, string clientSecret)
{
    public const string TokenUrl = "https://id.twitch.tv/oauth2/token";
    public const string Api = "https://api.igdb.com/v4/";

    /// <summary>Búsqueda en HowLongToBeat para abrir en el navegador (es un enlace, la app no lee esa web).</summary>
    public static string HltbSearchUrl(string title) => $"https://howlongtobeat.com/?q={Uri.EscapeDataString(TitleMatch.SearchQuery(title))}";

    // El token dura unas semanas: se guarda en memoria mientras la app está abierta (nunca en disco).
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static (string clientId, string token, DateTime until)? _token;

    // IGDB admite 4 peticiones por segundo: se deja un margen entre una y otra.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _last = DateTime.MinValue;
    public static TimeSpan MinInterval { get; set; } = TimeSpan.FromMilliseconds(280);

    private async Task<string> TokenAsync(bool renew, CancellationToken ct)
    {
        await TokenLock.WaitAsync(ct);
        try
        {
            if (!renew && _token is { } t && t.clientId == clientId && t.until > DateTime.UtcNow) return t.token;
            var url = $"{TokenUrl}?client_id={Uri.EscapeDataString(clientId.Trim())}&client_secret={Uri.EscapeDataString(clientSecret.Trim())}&grant_type=client_credentials";
            using var resp = await http.PostAsync(url, null, ct);
            if (resp.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ConnectorException("Twitch ha rechazado el Client ID o el secreto de IGDB. Revísalos en Ajustes › Cuentas.");
            if (!resp.IsSuccessStatusCode) throw new ConnectorException($"Twitch no ha dado acceso a IGDB (error {(int)resp.StatusCode}).");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var token = doc.RootElement.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            if (string.IsNullOrEmpty(token)) throw new ConnectorException("Twitch no ha dado acceso a IGDB.");
            var secs = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt64(out var s) ? s : 3600;
            _token = (clientId, token, DateTime.UtcNow.AddSeconds(Math.Max(60, secs - 300)));
            return token;
        }
        finally { TokenLock.Release(); }
    }

    private async Task<JsonDocument> PostAsync(string endpoint, string body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await TokenAsync(renew: attempt > 0, ct);
            await Gate.WaitAsync(ct);
            HttpResponseMessage resp;
            try
            {
                var wait = _last + MinInterval - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                using var req = new HttpRequestMessage(HttpMethod.Post, Api + endpoint) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
                req.Headers.TryAddWithoutValidation("Client-ID", clientId.Trim());
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                req.Headers.Accept.ParseAdd("application/json");
                resp = await http.SendAsync(req, ct);
                _last = DateTime.UtcNow;
            }
            finally { Gate.Release(); }

            using (resp)
            {
                // Token caducado o revocado: se pide otro una vez.
                if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
                if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new ConnectorException("IGDB ha rechazado el acceso. Revisa el Client ID y el secreto en Ajustes › Cuentas.");
                if ((int)resp.StatusCode == 429)
                {
                    if (attempt < 3) { await Task.Delay(1000 * (attempt + 1), ct); continue; }
                    throw new ConnectorException("IGDB está recibiendo demasiadas peticiones. Prueba otra vez dentro de un momento.");
                }
                if (!resp.IsSuccessStatusCode) throw new ConnectorException($"IGDB ha respondido con un error ({(int)resp.StatusCode}).");
                return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            }
        }
    }

    /// <summary>Busca el juego y devuelve los resultados con duración, el más probable primero (hasta 5).</summary>
    public async Task<List<IgdbDuration>> FindAsync(string title, string? platforms = null, CancellationToken ct = default)
    {
        var q = TitleMatch.SearchQuery(title).Replace("\"", " ").Replace("\\", " ").Trim();
        if (q.Length == 0) return new();
        using var games = await PostAsync("games",
            $"search \"{q}\"; fields id,name,slug,first_release_date,platforms.abbreviation,platforms.name; limit 15;", ct);
        var found = ParseGames(games.RootElement);
        if (found.Count == 0) return new();
        var ids = string.Join(",", found.Select(g => g.Id));
        using var times = await PostAsync("game_time_to_beats", $"fields game_id,hastily,normally,completely,count; where game_id = ({ids}); limit 50;", ct);
        return Rank(title, platforms, found, ParseTimes(times.RootElement));
    }

    // ───────────────────────────── lectura y elección (sin red: se prueban aparte) ─────────────────────────────

    public sealed record GameInfo(long Id, string Name, string? Slug, int? Year, List<string> Platforms);
    public sealed record Times(long GameId, int? Hastily, int? Normally, int? Completely, int Count);

    public static List<GameInfo> ParseGames(JsonElement root)
    {
        var list = new List<GameInfo>();
        if (root.ValueKind != JsonValueKind.Array) return list;
        foreach (var g in root.EnumerateArray())
        {
            if (!g.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id)) continue;
            var name = g.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
            if (name.Length == 0) continue;
            int? year = g.TryGetProperty("first_release_date", out var d) && d.TryGetInt64(out var unix) ? DateTimeOffset.FromUnixTimeSeconds(unix).Year : null;
            var plats = new List<string>();
            if (g.TryGetProperty("platforms", out var ps) && ps.ValueKind == JsonValueKind.Array)
                foreach (var p in ps.EnumerateArray())
                    if (p.ValueKind == JsonValueKind.Object)
                    {
                        var ab = p.TryGetProperty("abbreviation", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                        var nm = p.TryGetProperty("name", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
                        if ((ab ?? nm) is { Length: > 0 } x) plats.Add(x);
                    }
            var slug = g.TryGetProperty("slug", out var sl) && sl.ValueKind == JsonValueKind.String ? sl.GetString() : null;
            list.Add(new GameInfo(id, name, slug, year, plats));
        }
        return list;
    }

    public static Dictionary<long, Times> ParseTimes(JsonElement root)
    {
        var map = new Dictionary<long, Times>();
        if (root.ValueKind != JsonValueKind.Array) return map;
        static int? I(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out var i) && i > 0 ? i : null;
        foreach (var t in root.EnumerateArray())
        {
            if (!t.TryGetProperty("game_id", out var g) || !g.TryGetInt64(out var id)) continue;
            map[id] = new Times(id, I(t, "hastily"), I(t, "normally"), I(t, "completely"), I(t, "count") ?? 0);
        }
        return map;
    }

    /// <summary>Segundos → horas redondeadas a media hora (mínimo media hora).</summary>
    public static double? Hours(int? seconds) => seconds is > 0 ? Math.Max(0.5, Math.Round(seconds.Value / 1800.0) / 2) : null;

    /// <summary>Ordena: primero el título que coincide, luego el que comparte plataforma con la lista y el que tiene más votos.
    /// Solo devuelve los que tienen tiempo de historia.</summary>
    public static List<IgdbDuration> Rank(string title, string? platforms, List<GameInfo> games, Dictionary<long, Times> times)
    {
        var mine = (platforms ?? "").Split('/').Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToList();
        var list = new List<(IgdbDuration d, int score, int order)>();
        for (var i = 0; i < games.Count; i++)
        {
            var g = games[i];
            if (!times.TryGetValue(g.Id, out var t)) continue;
            var main = Hours(t.Normally ?? t.Hastily);
            if (main == null) continue;
            var exact = TitleMatch.Key(g.Name) == TitleMatch.Key(title);
            var same = exact || TitleMatch.IsSameGame(g.Name, title);
            var plat = mine.Count > 0 && g.Platforms.Any(p => mine.Any(m => SamePlatform(m, p.ToLowerInvariant())));
            var score = (exact ? 1000 : same ? 600 : 0) + (plat ? 100 : 0) + Math.Min(99, t.Count);
            list.Add((new IgdbDuration
            {
                Id = g.Id, Name = g.Name, Slug = g.Slug, Year = g.Year, Platforms = g.Platforms,
                Main = main, Full = Hours(t.Completely), Count = t.Count, Exact = same
            }, score, i));
        }
        return list.OrderByDescending(x => x.score).ThenBy(x => x.order).Take(5).Select(x => x.d).ToList();
    }

    private static bool SamePlatform(string mine, string igdb)
    {
        if (mine == igdb) return true;
        // «PC», «Steam», «Steam Deck»… → «PC» de IGDB; «Switch» → «Switch»/«Switch 2»; «PS5» → «PS5»
        string Norm(string p) => p switch
        {
            _ when p.Contains("steam") || p.Contains("epic") || p.Contains("gog") || p is "pc" or "windows" || p.Contains("ally") || p.Contains("legion") => "pc",
            _ when p.Contains("switch") => "switch",
            _ when p.Contains("xbox series") || p == "xsx" || p.Contains("series x") => "xsx",
            _ => p.Replace(" ", "")
        };
        return Norm(mine) == Norm(igdb);
    }
}

/// <summary>Rellena {ttb: …} en los juegos de una sección que aún no lo tienen. Solo usa coincidencias claras del título.</summary>
public static class DurationFiller
{
    public sealed record Change(string EntryId, string Title, string Field, string IgdbName);

    public sealed record Report(List<Change> Changes, List<string> NotFound, List<string> Unsure, int Skipped);

    public static async Task<Report> FillAsync(Section section, Func<string, string?, CancellationToken, Task<List<IgdbDuration>>> find,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var changes = new List<Change>(); var notFound = new List<string>(); var unsure = new List<string>();
        var todo = section.Entries.Where(e => e.Title.Trim().Length > 0 && Fields.Duration(e) == null).ToList();
        var skipped = section.Entries.Count() - todo.Count;
        var i = 0;
        foreach (var e in todo)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Buscando duraciones en IGDB… {++i} de {todo.Count}");
            var r = await find(e.Title, e.Platforms, ct);
            var best = r.FirstOrDefault();
            if (best == null) notFound.Add(e.Title.Trim());
            else if (!best.Exact || best.Field == null) unsure.Add(e.Title.Trim());
            else changes.Add(new Change(e.Id, e.Title.Trim(), best.Field, best.Name));
        }
        return new Report(changes, notFound, unsure, skipped);
    }
}
