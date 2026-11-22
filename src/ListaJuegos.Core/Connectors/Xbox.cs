using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;

namespace ListaJuegos.Core.Connectors;

/// <summary>Juegos de Xbox/Game Pass instalados en carpetas «XboxGames» de cualquier unidad.</summary>
public static class XboxLocal
{
    public static ConnectorResult Scan(IEnumerable<string>? roots = null)
    {
        var res = new ConnectorResult { Source = "xbox" };
        roots ??= DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Select(d => Path.Combine(d.RootDirectory.FullName, "XboxGames"));
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                if (!Directory.Exists(Path.Combine(dir, "Content"))) continue;
                var cfg = Directory.EnumerateFiles(Path.Combine(dir, "Content"), "MicrosoftGame.config", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                    .FirstOrDefault();
                if (cfg == null) continue;
                try
                {
                    var c = ReadConfig(cfg);
                    if (c != null) res.Games.Add(c);
                }
                catch (Exception e) when (e is IOException or System.Xml.XmlException or UnauthorizedAccessException) { }
            }
        }
        if (res.Games.Count == 0) res.Message = "No se han encontrado juegos de Xbox instalados.";
        return res;
    }

    private static ImportCandidate? ReadConfig(string path)
    {
        var x = XDocument.Load(path);
        var game = x.Root;
        if (game == null) return null;
        XElement? El(string n) => game.Descendants().FirstOrDefault(e => e.Name.LocalName == n);
        var visuals = El("ShellVisuals");
        var name = visuals?.Attribute("DefaultDisplayName")?.Value;
        var identity = El("Identity")?.Attribute("Name")?.Value;
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)));
        if (name != null && Guid.TryParse(name, out _) && identity != null)
            name = identity.Contains('.') ? identity[(identity.IndexOf('.') + 1)..] : identity;
        var storeId = El("StoreId")?.Value.Trim();
        var content = Path.GetDirectoryName(path)!;
        string? image = null;
        foreach (var attr in new[] { "Square150x150Logo", "Square480x480Logo", "StoreLogo", "Square44x44Logo" })
        {
            var rel = visuals?.Attribute(attr)?.Value;
            if (rel == null) continue;
            var full = Path.Combine(content, rel);
            if (File.Exists(full)) { image = full; break; }
        }
        return new ImportCandidate
        {
            Source = "xbox", Title = name ?? "Juego de Xbox", StoreKey = string.IsNullOrEmpty(storeId) ? null : "xbox", StoreId = storeId,
            Installed = true, Platform = "Xbox PC", LocalImage = image
        };
    }
}

/// <summary>Historial de Xbox con logros a través de OpenXBL (xbl.io), un servicio de terceros con clave gratuita.</summary>
public sealed class OpenXblClient(HttpClient http)
{
    public async Task<ConnectorResult> GetTitleHistoryAsync(string apiKey, CancellationToken ct = default)
    {
        var res = new ConnectorResult { Source = "xbox" };
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://xbl.io/api/v2/achievements");
        req.Headers.Add("X-Authorization", apiKey.Trim());
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.AcceptLanguage.ParseAdd("es-ES");
        using var r = await http.SendAsync(req, ct);
        if (r.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new ConnectorException("OpenXBL ha rechazado la clave. Revísala en Ajustes.");
        if (!r.IsSuccessStatusCode) throw new ConnectorException($"OpenXBL respondió con un error ({(int)r.StatusCode}).");
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (root.TryGetProperty("content", out var content)) root = content; // algunas respuestas van envueltas
        if (!root.TryGetProperty("titles", out var titles)) return res;

        foreach (var t in titles.EnumerateArray())
        {
            var type = t.TryGetProperty("type", out var ty) ? ty.GetString() : "Game";
            if (type != null && !type.Equals("Game", StringComparison.OrdinalIgnoreCase)) continue;
            var c = new ImportCandidate { Source = "xbox", Title = t.GetProperty("name").GetString() ?? "", Platform = "Xbox" };
            if (t.TryGetProperty("devices", out var dev))
            {
                var d = dev.EnumerateArray().Select(x => x.GetString()).ToList();
                c.Platform = d.Contains("PC") && !d.Any(x => x != null && x.StartsWith("Xbox")) ? "Xbox PC" : "Xbox";
            }
            if (t.TryGetProperty("displayImage", out var img)) c.CoverUrl = img.GetString()?.Replace("http://", "https://");
            if (t.TryGetProperty("achievement", out var a))
            {
                if (a.TryGetProperty("progressPercentage", out var pp) && pp.ValueKind == JsonValueKind.Number) c.Achievements = pp.GetDouble();
                else if (a.TryGetProperty("currentAchievements", out var cur) && a.TryGetProperty("totalAchievements", out var tot) && tot.GetInt32() > 0)
                    c.Achievements = Math.Round(100.0 * cur.GetInt32() / tot.GetInt32());
            }
            if (t.TryGetProperty("titleHistory", out var h) && h.TryGetProperty("lastTimePlayed", out var lt) && DateTime.TryParse(lt.GetString(), out var when))
                c.LastPlayed = when;
            foreach (var key in new[] { "productId", "modernTitleId" })
                if (t.TryGetProperty(key, out var pid) && pid.GetString() is { Length: 12 } p) { c.StoreKey = "xbox"; c.StoreId = p; break; }
            if (c.StoreId == null && t.TryGetProperty("detail", out var det) && det.ValueKind == JsonValueKind.Object
                && det.TryGetProperty("productId", out var dp) && dp.GetString() is { Length: 12 } p2) { c.StoreKey = "xbox"; c.StoreId = p2; }
            if (c.Title.Length > 0) res.Games.Add(c);
        }
        return res;
    }
}
