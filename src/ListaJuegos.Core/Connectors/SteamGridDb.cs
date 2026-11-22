using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ListaJuegos.Core.Connectors;

/// <summary>
/// SteamGridDB (https://www.steamgriddb.com): carátulas verticales hechas por la comunidad, también de juegos que no
/// están en Steam (Nintendo, PlayStation…). Necesita una clave gratuita de la API v2 (Bearer).
/// </summary>
public sealed class SteamGridDbClient(HttpClient http, string apiKey)
{
    public const string Api = "https://www.steamgriddb.com/api/v2";

    /// <summary>Una carátula elegida: su id, la URL de la imagen y su extensión.</summary>
    public sealed record Grid(long Id, string Url, string Extension);

    /// <summary>Busca el juego por nombre y devuelve su id de SteamGridDB si el nombre coincide de verdad.</summary>
    public async Task<(long id, string name)?> FindGameAsync(string title, CancellationToken ct = default)
    {
        var q = TitleMatch.SearchQuery(title);
        if (q.Length < 2) return null;
        using var doc = await GetJson($"{Api}/search/autocomplete/{Uri.EscapeDataString(q)}", ct);
        if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return null;
        foreach (var g in data.EnumerateArray())
        {
            var name = g.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (TitleMatch.IsSameGame(q, name) && g.TryGetProperty("id", out var id)) return (id.GetInt64(), name);
        }
        return null;
    }

    /// <summary>Mejor carátula vertical 2:3 (600×900, estática, sin desnudos ni humor) de un juego por su id de SteamGridDB.</summary>
    public Task<Grid?> GridForGameAsync(long gameId, CancellationToken ct = default) => BestGridAsync($"game/{gameId}", ct);

    /// <summary>Lo mismo, a partir del appid de Steam (no hace falta buscar por nombre).</summary>
    public Task<Grid?> GridForSteamAsync(string appId, CancellationToken ct = default) => BestGridAsync($"steam/{Uri.EscapeDataString(appId)}", ct);

    /// <summary>Comprueba la clave con una búsqueda cualquiera. Lanza ConnectorException si la rechaza.</summary>
    public async Task CheckKeyAsync(CancellationToken ct = default) => (await GetJson($"{Api}/search/autocomplete/portal", ct))?.Dispose();

    private async Task<Grid?> BestGridAsync(string path, CancellationToken ct)
    {
        // Primero el tamaño de póster de Steam; si no hay, los otros tamaños verticales que admite la API.
        foreach (var dims in new[] { "600x900", "342x482,660x930" })
        {
            using var doc = await GetJson($"{Api}/grids/{path}?dimensions={dims}&types=static&nsfw=false&humor=false", ct);
            if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) continue;
            var best = data.EnumerateArray()
                .Where(g => !Flag(g, "nsfw") && !Flag(g, "humor") && g.TryGetProperty("url", out _))
                .Where(g => !g.TryGetProperty("width", out var w) || !g.TryGetProperty("height", out var h) || h.GetInt32() > w.GetInt32())
                .OrderByDescending(g => g.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0)
                .Select(g => new Grid(g.GetProperty("id").GetInt64(), g.GetProperty("url").GetString()!, Ext(g)))
                .FirstOrDefault(g => Uri.TryCreate(g.Url, UriKind.Absolute, out var u) && u.Scheme == "https");
            if (best != null) return best;
        }
        return null;
    }

    private static bool Flag(JsonElement g, string name) => g.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Ext(JsonElement g)
    {
        var mime = g.TryGetProperty("mime", out var m) ? m.GetString() : null;
        return mime switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/jpeg" => ".jpg",
            _ => Path.GetExtension(new Uri(g.GetProperty("url").GetString()!).AbsolutePath) is { Length: > 1 and <= 5 } e ? e.ToLowerInvariant() : ".jpg"
        };
    }

    /// <summary>null si el juego no existe (404); excepción si la clave no vale o el servicio falla.</summary>
    private async Task<JsonDocument?> GetJson(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var r = await http.SendAsync(req, ct);
        if (r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ConnectorException("SteamGridDB ha rechazado la clave. Revísala en Ajustes.");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode) throw new ConnectorException($"SteamGridDB respondió con un error ({(int)r.StatusCode}).");
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
    }
}
