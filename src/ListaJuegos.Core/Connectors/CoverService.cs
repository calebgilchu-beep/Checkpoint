using System.Collections.Concurrent;
using System.Text.Json;

namespace ListaJuegos.Core.Connectors;

/// <summary>
/// Portadas: con clave de SteamGridDB las busca allí primero (carátulas 2:3, también de juegos que no están en Steam);
/// si no hay clave o no encuentra ninguna, en la tienda de Steam (sin clave). Las guarda en caché en disco.
/// </summary>
public sealed class CoverService
{
    private readonly HttpClient _http;
    private readonly SteamClient _steam;
    private readonly string _dir;
    private readonly string _indexPath;
    private readonly ConcurrentDictionary<string, IndexItem> _index;
    private readonly SemaphoreSlim _gate = new(3);
    private readonly SemaphoreSlim _save = new(1);

    public sealed class IndexItem
    {
        public string? AppId { get; set; }
        public string? File { get; set; }
        public DateTime Checked { get; set; }
        /// <summary>Ya se preguntó a SteamGridDB por este juego (con una clave válida).</summary>
        public bool Sgdb { get; set; }
    }

    /// <summary>Clave de la API de SteamGridDB; null para usar solo la tienda de Steam.</summary>
    public string? SgdbKey { get; set; }

    public CoverService(HttpClient http, string cacheDir)
    {
        _http = http;
        _steam = new SteamClient(http);
        _dir = cacheDir;
        Directory.CreateDirectory(_dir);
        _indexPath = Path.Combine(_dir, "index.json");
        try
        {
            _index = File.Exists(_indexPath)
                ? new(JsonSerializer.Deserialize<Dictionary<string, IndexItem>>(File.ReadAllText(_indexPath)) ?? new())
                : new();
        }
        catch (JsonException) { _index = new(); }
    }

    public string CacheDir => _dir;

    /// <summary>Devuelve el nombre del archivo en caché (relativo a la carpeta) o null si no hay portada.</summary>
    /// <param name="sgdbId">Id de juego de SteamGridDB elegido a mano ({sgdb:…}); manda sobre el appid y el título.</param>
    public async Task<string?> ResolveAsync(string title, string? steamId, string? xboxId, string? localImage, string? sgdbId = null, CancellationToken ct = default)
    {
        if (sgdbId != null && !long.TryParse(sgdbId, out _)) sgdbId = null;
        var key = sgdbId != null ? "sgdb:" + sgdbId : steamId != null ? "steam:" + steamId : xboxId != null ? "xbox:" + xboxId : "t:" + TitleMatch.Key(title);
        var sgdbKey = SgdbKey;
        _index.TryGetValue(key, out var hit);
        var cached = hit?.File != null && File.Exists(Path.Combine(_dir, hit.File)) ? hit.File : null;
        // Con clave de SteamGridDB se vuelve a buscar una vez lo que antes salió de Steam o no se encontró.
        var askSgdb = sgdbKey != null && localImage == null && hit?.Sgdb != true;
        if (hit != null && !askSgdb)
        {
            if (cached != null) return cached;
            if (hit.File == null && (DateTime.UtcNow - hit.Checked).TotalDays < 30 && localImage == null) return null;
        }

        await _gate.WaitAsync(ct);
        try
        {
            string? file = null;
            var appId = steamId ?? hit?.AppId;
            var sgdbDone = hit?.Sgdb == true;
            if (localImage != null && File.Exists(localImage))
            {
                file = Safe(key) + Path.GetExtension(localImage).ToLowerInvariant();
                File.Copy(localImage, Path.Combine(_dir, file), true);
            }
            else
            {
                if (askSgdb)
                {
                    try
                    {
                        file = await FromSteamGridDbAsync(new SteamGridDbClient(_http, sgdbKey!), title, steamId, sgdbId, ct);
                        sgdbDone = true;
                    }
                    catch (Exception e) when (e is HttpRequestException or ConnectorException or JsonException or TaskCanceledException) { /* se intenta otra vez otro día */ }
                }
                file ??= cached;
                if (file == null)
                {
                    if (appId == null)
                    {
                        try { appId = (await _steam.FindAppAsync(title, ct))?.appId; }
                        catch (Exception e) when (e is HttpRequestException or ConnectorException or JsonException or TaskCanceledException) { if (!sgdbDone) return null; }
                    }
                    if (appId != null)
                    {
                        foreach (var url in new[] { SteamClient.CoverUrl(appId), SteamClient.HeaderUrl(appId) })
                        {
                            var name = $"steam_{appId}{(url.Contains("header") ? "_h" : "")}.jpg";
                            if (await DownloadAsync(url, name, ct)) { file = name; break; }
                        }
                    }
                }
            }
            _index[key] = new IndexItem { AppId = appId, File = file, Checked = DateTime.UtcNow, Sgdb = sgdbDone };
            await SaveIndexAsync();
            return file;
        }
        finally { _gate.Release(); }
    }

    private async Task<string?> FromSteamGridDbAsync(SteamGridDbClient sgdb, string title, string? steamId, string? sgdbId, CancellationToken ct)
    {
        SteamGridDbClient.Grid? grid = null;
        if (sgdbId != null) grid = await sgdb.GridForGameAsync(long.Parse(sgdbId), ct);
        if (grid == null && sgdbId != null) return null;   // id elegido a mano sin carátulas: no se busca otro juego en SteamGridDB
        if (steamId != null) grid = await sgdb.GridForSteamAsync(steamId, ct);
        if (grid == null && await sgdb.FindGameAsync(title, ct) is { } game) grid = await sgdb.GridForGameAsync(game.id, ct);
        if (grid == null) return null;
        var name = $"sgdb_{grid.Id}{grid.Extension}";
        if (File.Exists(Path.Combine(_dir, name))) return name;
        return await DownloadAsync(grid.Url, name, ct) ? name : null;
    }

    private async Task<bool> DownloadAsync(string url, string name, CancellationToken ct)
    {
        try
        {
            using var r = await _http.GetAsync(url, ct);
            if (!r.IsSuccessStatusCode) return false;
            var bytes = await r.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length < 1024) return false;
            await File.WriteAllBytesAsync(Path.Combine(_dir, name), bytes, ct);
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { return false; }
    }

    /// <summary>appid de Steam deducido para ese título (si se buscó antes).</summary>
    public string? KnownAppId(string title) =>
        _index.TryGetValue("t:" + TitleMatch.Key(title), out var i) ? i.AppId : null;

    private async Task SaveIndexAsync()
    {
        await _save.WaitAsync();
        try { await File.WriteAllTextAsync(_indexPath, JsonSerializer.Serialize(_index)); }
        catch (IOException) { }
        finally { _save.Release(); }
    }

    private static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
}
