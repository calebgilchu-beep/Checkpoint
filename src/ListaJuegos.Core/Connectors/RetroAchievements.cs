using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ListaJuegos.Core.Connectors;

/// <summary>Juegos jugados en RetroAchievements (retroachievements.org) con la Web API pública:
/// usuario + Web API Key (Ajustes › Claves de tu perfil de RetroAchievements).</summary>
public sealed class RetroAchievementsClient(HttpClient http)
{
    public const string Api = "https://retroachievements.org/API/";
    public const string Media = "https://media.retroachievements.org";
    private const int PageSize = 500;
    private const int MaxPages = 20;          // 10 000 juegos: de sobra

    public static string GameUrl(string id) => $"https://retroachievements.org/game/{id}";

    public async Task<ConnectorResult> GetLibraryAsync(string user, string key, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        user = user.Trim();
        if (user.Length == 0) throw new ConnectorException("Falta tu usuario de RetroAchievements (Ajustes › Cuentas).");
        var auth = $"u={Uri.EscapeDataString(user)}&y={Uri.EscapeDataString(key.Trim())}";
        var games = new Dictionary<string, ImportCandidate>();

        // 1) Progreso de todos los juegos con logros desbloqueados (paginado).
        for (var page = 0; page < MaxPages; page++)
        {
            using var doc = await GetJson($"{Api}API_GetUserCompletionProgress.php?{auth}&c={PageSize}&o={page * PageSize}", ct);
            var root = doc.RootElement;
            var results = root.ValueKind == JsonValueKind.Array ? root
                        : root.TryGetProperty("Results", out var r) ? r : default;
            if (results.ValueKind != JsonValueKind.Array) break;
            var n = 0;
            foreach (var g in results.EnumerateArray()) { n++; Add(games, ParseProgress(g)); }
            progress?.Report($"Leyendo RetroAchievements… {games.Count} juegos");
            var total = root.ValueKind == JsonValueKind.Object ? Int(root, "Total") : null;
            if (n < PageSize || (total is { } t && (page + 1) * PageSize >= t)) break;
        }

        // 2) Jugados hace poco: añade la fecha de la última sesión (y juegos aún sin logros).
        using (var recent = await GetJson($"{Api}API_GetUserRecentlyPlayedGames.php?{auth}&c=50", ct))
        {
            if (recent.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var g in recent.RootElement.EnumerateArray()) Add(games, ParseRecent(g));
        }

        return new ConnectorResult
        {
            Source = "retroachievements",
            Games = games.Values.ToList(),
            Message = games.Count == 0 ? "RetroAchievements no tiene juegos jugados en esa cuenta." : null
        };
    }

    // ───────────────────────────── lectura de las respuestas ─────────────────────────────

    /// <summary>Un elemento de API_GetUserCompletionProgress.</summary>
    public static ImportCandidate? ParseProgress(JsonElement g)
    {
        var c = Base(g);
        if (c == null) return null;
        var max = Int(g, "MaxPossible") ?? Int(g, "NumPossibleAchievements");
        var soft = Int(g, "NumAwarded") ?? Int(g, "NumAchieved");
        var hard = Int(g, "NumAwardedHardcore") ?? Int(g, "NumAchievedHardcore");
        SetProgress(c, max, soft, hard);
        c.LastPlayed = Date(g, "MostRecentAwardedDate") ?? Date(g, "HighestAwardDate");

        var award = Str(g, "HighestAwardKind")?.ToLowerInvariant();
        if (award is "mastered" or "completed") c.Achievements = 100;
        if (award == "mastered") c.Hardcore = 100;
        if (award != null && award.StartsWith("beaten")) c.Beaten = true;
        return c;
    }

    /// <summary>Un elemento de API_GetUserRecentlyPlayedGames.</summary>
    public static ImportCandidate? ParseRecent(JsonElement g)
    {
        var c = Base(g);
        if (c == null) return null;
        SetProgress(c, Int(g, "NumPossibleAchievements") ?? Int(g, "MaxPossible"),
                       Int(g, "NumAchieved") ?? Int(g, "NumAwarded"),
                       Int(g, "NumAchievedHardcore") ?? Int(g, "NumAwardedHardcore"));
        c.LastPlayed = Date(g, "LastPlayed");
        return c;
    }

    private static ImportCandidate? Base(JsonElement g)
    {
        if (g.ValueKind != JsonValueKind.Object) return null;
        var id = Int(g, "GameID")?.ToString(CultureInfo.InvariantCulture);
        var title = Str(g, "Title");
        if (id == null || string.IsNullOrWhiteSpace(title)) return null;
        var console = Str(g, "ConsoleName");
        var icon = Str(g, "ImageIcon");
        return new ImportCandidate
        {
            Source = "retroachievements", Title = RomName.Clean(title), StoreKey = "ra", StoreId = id,
            Platform = RetroPlatforms.Find(console) ?? console?.Trim(), Note = "emulación",
            CoverUrl = icon != null && icon.StartsWith('/') ? Media + icon : null
        };
    }

    private static void SetProgress(ImportCandidate c, int? max, int? soft, int? hard)
    {
        if (max is not > 0) return;
        if (soft is { } s) c.Achievements = Math.Round(100.0 * Math.Max(s, hard ?? 0) / max.Value);
        if (hard is > 0) c.Hardcore = Math.Round(100.0 * hard.Value / max.Value);
    }

    /// <summary>Une dos lecturas del mismo juego: lo más alto de cada dato y la fecha más reciente.</summary>
    private static void Add(Dictionary<string, ImportCandidate> games, ImportCandidate? c)
    {
        if (c?.StoreId == null) return;
        if (!games.TryGetValue(c.StoreId, out var g)) { games[c.StoreId] = c; return; }
        if (c.Achievements > (g.Achievements ?? -1)) g.Achievements = c.Achievements;
        if (c.Hardcore > (g.Hardcore ?? -1)) g.Hardcore = c.Hardcore;
        if (c.LastPlayed > (g.LastPlayed ?? DateTime.MinValue)) g.LastPlayed = c.LastPlayed;
        g.Beaten |= c.Beaten;
        g.CoverUrl ??= c.CoverUrl;
        g.Platform ??= c.Platform;
    }

    private async Task<JsonDocument> GetJson(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var r = await http.SendAsync(req, ct);
        if (r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ConnectorException("RetroAchievements ha rechazado el usuario o la Web API Key. Revísalos en Ajustes › Cuentas.");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ConnectorException("RetroAchievements no encuentra ese usuario. Revísalo en Ajustes › Cuentas.");
        if ((int)r.StatusCode == 429)
            throw new ConnectorException("RetroAchievements ha recibido demasiadas peticiones. Prueba de nuevo dentro de un rato.");
        if (!r.IsSuccessStatusCode) throw new ConnectorException($"RetroAchievements respondió con un error ({(int)r.StatusCode}).");
        var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        // Algunas versiones de la API devuelven 200 con {"message": "..."} o {"error": "..."} ante una clave no válida.
        if (doc.RootElement.ValueKind == JsonValueKind.Object && !doc.RootElement.TryGetProperty("Results", out _)
            && (Str(doc.RootElement, "message") ?? Str(doc.RootElement, "error")) is { } msg)
        {
            doc.Dispose();
            throw new ConnectorException(msg.Contains("auth", StringComparison.OrdinalIgnoreCase) || msg.Contains("key", StringComparison.OrdinalIgnoreCase)
                ? "RetroAchievements ha rechazado el usuario o la Web API Key. Revísalos en Ajustes › Cuentas."
                : $"RetroAchievements: {msg}");
        }
        return doc;
    }

    // Los números a veces llegan como texto («"12"») en la API antigua.
    private static int? Int(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Fechas «2024-05-01 10:00:00» (UTC) o ISO 8601 «2023-02-08T22:26:02+00:00», en hora local.</summary>
    private static DateTime? Date(JsonElement e, string name)
    {
        var s = Str(e, name);
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d.ToLocalTime() : null;
    }
}
