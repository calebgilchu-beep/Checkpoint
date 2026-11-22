using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using ListaJuegos.Core;
using ListaJuegos.Core.Connectors;
using ListaJuegos.Core.Stats;
using ListaJuegos.Core.Suggest;
using ListaJuegos.Core.Sync;

namespace ListaJuegos.App;

/// <summary>Órdenes que llegan de la interfaz (app.js) y sus respuestas.</summary>
public sealed class Bridge
{
    private readonly IHostUi _win;
    private readonly AppSettings _s;
    private readonly HttpClient _http;
    private readonly CoverService _covers;
    private readonly HashSet<string> _backedUp = new(StringComparer.OrdinalIgnoreCase);
    private FileChangeWatcher? _watch;
    public string? StartupFile { get; set; }

    public Bridge(IHostUi win, AppSettings settings, HttpClient http)
    {
        _win = win;
        _s = settings;
        _http = http;
        _covers = new CoverService(http, CoversFolder) { SgdbKey = settings.SgdbKey };
    }

    public static string CoversFolder => Path.Combine(AppSettings.DataFolder, "covers");

    public async Task<object?> HandleAsync(string cmd, JsonElement a)
    {
        switch (cmd)
        {
            case "init":
                object? file = null, resume = null;
                if (StartupFile != null && File.Exists(StartupFile)) file = OpenPath(StartupFile);
                else if (_s.Onboarded && _s.Recent.FirstOrDefault(File.Exists) is { } last)
                {
                    // Última lista abierta: se abre directamente, se pregunta (lo hace la interfaz) o nada, según Ajustes
                    if (_s.Resume == "always")
                    {
                        try { file = OpenPath(last); }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { resume = new { path = last }; }
                    }
                    else if (_s.Resume != "never") resume = new { path = last };
                }
                return new { settings = _s.Public(), recent = _s.RecentList(), sagas = Sagas.All, file, resume, version = typeof(Bridge).Assembly.GetName().Version?.ToString(3) };

            case "file.new":
                Watch(null);
                return new { doc = DocumentService.NewTemplate(DateTime.Now) };

            case "file.open":
            {
                var path = Str(a, "path");
                if (path == null)
                {
                    path = _win.PickOpen(Lang.T("Abrir lista de juegos", "Open game list", "Abrir lista de jogos"), new[]
                    {
                        new FileFilter(Lang.T("Listas de juegos", "Game lists", "Listas de jogos"), "txt", "md", "markdown"),
                        new FileFilter(Lang.T("Texto", "Text", "Texto"), "txt"),
                        new FileFilter("Markdown", "md", "markdown"),
                        new FileFilter(Lang.T("Todos los archivos", "All files", "Todos os arquivos"), "*")
                    }, InitialDir());
                    if (path == null) return null;
                }
                if (!File.Exists(path)) throw new UserError("Ese archivo ya no existe.");
                return OpenPath(path);
            }

            case "file.save":
            {
                var doc = Doc(a);
                var current = Str(a, "path");
                var asKind = Str(a, "as");
                var path = current;
                if (path == null || asKind != null)
                {
                    var kind = asKind ?? (current != null ? (DocumentService.KindFromPath(current) == FileKind.Md ? "md" : "txt") : "txt");
                    var baseName = current != null ? Path.GetFileNameWithoutExtension(current) : "Juegos que me he pasado";
                    path = _win.PickSave(Lang.T("Guardar lista de juegos", "Save game list", "Salvar lista de jogos"),
                        new[] { new FileFilter(Lang.T("Texto", "Text", "Texto"), "txt"), new FileFilter("Markdown", "md") },
                        kind == "md" ? 2 : 1, kind == "md" ? ".md" : ".txt", baseName + (kind == "md" ? ".md" : ".txt"),
                        (current != null ? Path.GetDirectoryName(current) : null) ?? InitialDir());
                    if (path == null) return null;
                }
                if (_s.UpdateDate) DocumentService.TouchHeaderDate(doc, DateTime.Now);
                ApplyMarkdownDefaults(doc);
                bool backup;
                lock (_backedUp) backup = _s.Backup && _backedUp.Add(Path.GetFullPath(path));
                SaveResult res;
                // Escritura propia: el vigilante no la confunde con un cambio externo.
                using (_watch != null && string.Equals(_watch.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) ? _watch.OwnWrite() : null)
                    res = DocumentService.Save(doc, path, backup);
                Watch(path);
                _s.AddRecent(path); _s.Save();
                return new
                {
                    doc, path, kind = DocumentService.KindFromPath(path) == FileKind.Md ? "md" : "txt",
                    encoding = doc.Layout.EncodingName, newLine = NewLineName(doc), upgraded = res.EncodingUpgraded, recent = _s.RecentList()
                };
            }

            case "file.serialize":
            {
                var doc = Doc(a);
                ApplyMarkdownDefaults(doc);
                return new { text = DocumentService.Serialize(doc, Str(a, "kind") == "md" ? FileKind.Md : FileKind.Txt) };
            }

            case "file.parse":
                return new { doc = DocumentService.Parse(Str(a, "text") ?? "", Str(a, "kind") == "md" ? FileKind.Md : FileKind.Txt) };

            case "state.dirty":
                _win.SetDirty(Bool(a, "dirty"), Str(a, "title") ?? "Checkpoint");
                return null;

            case "settings.set":
                ApplySettings(a);
                _s.Save();
                if (a.TryGetProperty("theme", out _)) _win.ApplyTitleBarTheme();
                return _s.Public();

            case "secrets.set":
                if (Bool(a, "clear")) { _s.SteamKeyProtected = null; _s.XblKeyProtected = null; _s.SgdbKeyProtected = null; _s.RaKeyProtected = null; _s.IgdbSecretProtected = null; }
                else
                {
                    if (Str(a, "steamKey") is { Length: > 0 } sk) _s.SteamKey = sk;
                    if (a.TryGetProperty("steamId", out var sid) && sid.ValueKind == JsonValueKind.String) _s.SteamId = sid.GetString()!.Trim();
                    if (Str(a, "xblKey") is { Length: > 0 } xk) _s.XblKey = xk;
                    if (Str(a, "sgdbKey") is { Length: > 0 } gk) _s.SgdbKey = gk;
                    if (a.TryGetProperty("raUser", out var ru) && ru.ValueKind == JsonValueKind.String) _s.RaUser = ru.GetString()!.Trim();
                    if (Str(a, "raKey") is { Length: > 0 } rk) _s.RaKey = rk;
                    if (a.TryGetProperty("igdbClientId", out var ic) && ic.ValueKind == JsonValueKind.String) _s.IgdbClientId = ic.GetString()!.Trim();
                    if (Str(a, "igdbSecret") is { Length: > 0 } isec) _s.IgdbSecret = isec;
                }
                _s.Save();
                _covers.SgdbKey = _s.SgdbKey;
                return _s.Public();

            case "sgdb.check":
            {
                // Comprueba la clave guardada con una búsqueda; devuelve el error en español si la rechaza.
                var key = _s.SgdbKey;
                if (key == null) return new { ok = false, error = "No hay clave de SteamGridDB." };
                try { await new SteamGridDbClient(_http, key).CheckKeyAsync(); return new { ok = true, error = (string?)null }; }
                catch (ConnectorException e) { return new { ok = false, error = (string?)e.Message }; }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return new { ok = false, error = (string?)"No se pudo conectar con SteamGridDB." }; }
            }

            case "open.url":
            {
                var url = Str(a, "url");
                if (url != null && Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
                    _win.OpenUrl(u.AbsoluteUri);
                return null;
            }

            case "covers.resolve":
                if (!_s.Covers) return null;
                foreach (var it in a.GetProperty("items").EnumerateArray())
                {
                    var key = Str(it, "key")!; var title = Str(it, "title") ?? ""; var steam = Str(it, "steam"); var xbox = Str(it, "xbox"); var sgdb = Str(it, "sgdb");
                    _ = Task.Run(async () =>
                    {
                        string? f = null;
                        try { f = await _covers.ResolveAsync(title, steam, xbox, null, sgdb); } catch { /* sin portada */ }
                        _win.PostEvent("cover", new { key, url = f != null ? _win.CoverUrl(f) : null });
                    });
                }
                return null;

            case "steam.find":
            {
                var r = await new SteamClient(_http).FindAppAsync(Str(a, "title") ?? "");
                return r == null ? null : new { appId = r.Value.appId, name = r.Value.name };
            }

            case "suggest.category":
            {
                var (name, paren) = ImportPlanner.SuggestCategory(Doc(a), Str(a, "title") ?? "");
                return new { name, paren };
            }

            case "next.suggest":
            {
                var length = Str(a, "length") switch { "short" => GameLength.Short, "medium" => GameLength.Medium, "long" => GameLength.Long, _ => GameLength.Any };
                var picks = NextGame.Suggest(Doc(a), new NextFilter(length, Bool(a, "sagaOnly"), Str(a, "platform")));
                return new { picks };
            }

            case "stats.compute":
                return LibraryStats.Compute(Doc(a));

            case "stats.year":
            {
                var year = a.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : DateTime.Now.Year;
                return LibraryStats.Year(Doc(a), year);
            }

            case "export.png":
                return ExportPng(Str(a, "data") ?? "", Str(a, "name") ?? "Resumen.png");

            case "ttb.lookup":
            {
                // Duración de un juego en IGDB (solo al pulsar «Buscar duración»: se envía el título)
                var found = await Igdb().FindAsync(Str(a, "title") ?? "", Str(a, "platforms"));
                return new { results = found.Select(Ttb).ToList() };
            }

            case "ttb.fill":
            {
                // Rellena {ttb: …} en los juegos de la sección que no lo tienen; la interfaz aplica los cambios (un solo «Deshacer»)
                var doc = Doc(a);
                var sec = doc.Sections.ElementAtOrDefault(a.TryGetProperty("section", out var si) && si.TryGetInt32(out var n) ? n : -1)
                          ?? throw new UserError("No se encuentra la sección.");
                var client = Igdb();
                return await DurationFiller.FillAsync(sec, (t, p, ct) => client.FindAsync(t, p, ct),
                    new Progress<string>(t => _win.PostEvent("progress", new { text = t })));
            }

            case "igdb.check":
            {
                // Comprueba el Client ID y el secreto con una búsqueda de prueba
                try { await Igdb().FindAsync("Celeste"); return new { ok = true, error = (string?)null }; }
                catch (ConnectorException e) { return new { ok = false, error = (string?)e.Message }; }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return new { ok = false, error = (string?)"No se pudo conectar con IGDB." }; }
            }

            case "import.scan":
                return await ScanAsync(Doc(a), a.GetProperty("sources").EnumerateArray().Select(x => x.GetString()!).ToHashSet(), Bool(a, "achievements"));

            case "import.apply":
            {
                var doc = Doc(a);
                var items = a.GetProperty("items").Deserialize<List<ImportCandidate>>(Json.Options) ?? new();
                ApplyImport(doc, items);
                return new { doc };
            }

            case "import.refresh":
            {
                var doc = Doc(a);
                var sources = a.GetProperty("sources").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
                sources.UnionWith(new[] { "steamOnline", "xboxOnline", "steamLocal" });
                if (_s.RaKeyProtected != null && _s.RaUser.Length > 0) sources.Add("raOnline");
                var scan = await GatherAsync(sources, achievements: true);
                var changed = ImportPlanner.RefreshLinked(doc, scan.games);
                return new { doc, changed };
            }

            case "zoom":
            {
                var dir = a.TryGetProperty("dir", out var d) ? d.GetInt32() : 0;
                var z = dir == 0 ? 1.0 : Math.Clamp(Math.Round((_win.Zoom + dir * 0.1) * 10) / 10, 0.6, 2.0);
                _win.Zoom = z; _s.Zoom = z; _s.Save();
                _win.PostEvent("zoomChanged", new { factor = z });
                return null;
            }

            case "window.fullscreen":
                return new { on = _win.SetFullScreen(a.TryGetProperty("on", out var fs) && fs.ValueKind is JsonValueKind.True or JsonValueKind.False ? fs.GetBoolean() : !_win.FullScreen) };

            case "app.quit":
                _win.ForceClose();
                return null;
        }
        throw new UserError($"Orden desconocida: {cmd}");
    }

    // ───────────────────────────── archivos ─────────────────────────────

    private object OpenPath(string path)
    {
        path = Path.GetFullPath(path);
        var doc = DocumentService.Load(path);
        Watch(path);
        _s.AddRecent(path); _s.Save();
        return new
        {
            doc, path, kind = DocumentService.KindFromPath(path) == FileKind.Md ? "md" : "txt",
            encoding = doc.Layout.EncodingName, newLine = NewLineName(doc), recent = _s.RecentList()
        };
    }

    private static string NewLineName(GameDocument d) => d.Layout.NewLine == "\n" ? "LF" : "CRLF";

    /// <summary>Vigila el archivo abierto para avisar de cambios hechos fuera (OneDrive, Syncthing, otro editor).</summary>
    private void Watch(string? path)
    {
        if (path != null && _watch != null && string.Equals(_watch.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            _watch.Acknowledge();
            return;
        }
        _watch?.Dispose();
        _watch = null;
        if (path == null || !File.Exists(path)) return;
        try
        {
            var w = new FileChangeWatcher(path);
            w.Changed += x =>
            {
                // El próximo guardado vuelve a dejar un .bak: si se mantienen los cambios propios, la versión que llegó de fuera no se pierde.
                lock (_backedUp) _backedUp.Remove(x.FilePath);
                _win.PostEvent("fileChanged", new { path = x.FilePath });
            };
            _watch = w;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Carpetas que no admiten avisos (algunas unidades de red): la app funciona igual, sin recarga automática.
        }
    }

    /// <summary>Guarda el PNG del «Resumen del año» con el diálogo de Windows. Solo acepta un PNG de verdad.</summary>
    private object? ExportPng(string data, string name)
    {
        var comma = data.IndexOf(',');
        if (data.StartsWith("data:", StringComparison.Ordinal) && comma > 0) data = data[(comma + 1)..];
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data); } catch (FormatException) { throw new UserError("La imagen no es válida."); }
        byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (bytes.Length < sig.Length || !bytes.AsSpan(0, sig.Length).SequenceEqual(sig)) throw new UserError("La imagen no es válida.");

        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        if (!safe.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) safe += ".png";
        var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var path = _win.PickSave(Lang.T("Guardar imagen del resumen", "Save summary image", "Salvar imagem do resumo"),
            new[] { new FileFilter(Lang.T("Imagen PNG", "PNG image", "Imagem PNG"), "png") }, 1, ".png", safe,
            Directory.Exists(pics) ? pics : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (path == null) return null;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
        return new { path };
    }

    private string InitialDir()
    {
        var last = _s.Recent.FirstOrDefault(File.Exists);
        return last != null ? Path.GetDirectoryName(last)! : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void ApplyMarkdownDefaults(GameDocument doc)
    {
        if (doc.Layout.SourceKind != FileKind.Md) doc.Markdown = _s.Markdown;
    }

    private void ApplySettings(JsonElement a)
    {
        foreach (var p in a.EnumerateObject())
        {
            switch (p.Name)
            {
                case "theme": _s.Theme = p.Value.GetString() ?? "system"; break;
                case "language": _s.Language = p.Value.GetString() ?? "system"; Lang.Set(_s.Language); break;
                case "view": _s.View = p.Value.GetString() ?? "grid"; break;
                case "covers": _s.Covers = p.Value.GetBoolean(); break;
                case "coversAsked": _s.CoversAsked = p.Value.GetBoolean(); break;
                case "updateDate": _s.UpdateDate = p.Value.GetBoolean(); break;
                case "writeStoreIds": _s.WriteStoreIds = p.Value.GetBoolean(); break;
                case "backup": _s.Backup = p.Value.GetBoolean(); break;
                case "onboarded": _s.Onboarded = p.Value.GetBoolean(); break;
                case "resume": _s.Resume = p.Value.GetString() is "always" or "never" ? p.Value.GetString()! : "ask"; break;
                case "markdown": _s.Markdown = p.Value.Deserialize<MarkdownOptions>(Json.Options) ?? _s.Markdown; break;
            }
        }
    }

    // ───────────────────────────── importación ─────────────────────────────

    private async Task<(List<ImportCandidate> games, List<object> messages)> GatherAsync(HashSet<string> sources, bool achievements)
    {
        var results = new List<ConnectorResult>();
        var messages = new List<object>();
        void Progress(string t) => _win.PostEvent("progress", new { text = t });

        async Task Run(string name, Func<Task<ConnectorResult>> f)
        {
            try { results.Add(await f()); }
            catch (ConnectorException e) { messages.Add(new { source = name, ok = false, message = e.Message }); }
            catch (HttpRequestException) { messages.Add(new { source = name, ok = false, message = $"No se pudo conectar con {name}. Comprueba la conexión a Internet." }); }
            catch (TaskCanceledException) { messages.Add(new { source = name, ok = false, message = $"{name} ha tardado demasiado en responder." }); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { messages.Add(new { source = name, ok = false, message = $"{name}: {e.Message}" }); }
        }

        if (sources.Contains("steamLocal")) { Progress("Buscando juegos de Steam en este PC…"); await Run("Steam", () => Task.Run(() => SteamLocal.Scan())); }
        if (sources.Contains("xboxLocal")) { Progress("Buscando juegos de Xbox en este PC…"); await Run("Xbox", () => Task.Run(() => XboxLocal.Scan())); }
        if (sources.Contains("epicLocal")) { Progress("Buscando juegos de Epic Games…"); await Run("Epic Games", () => Task.Run(() => EpicLocal.Scan())); }
        if (sources.Contains("gogLocal")) await Run("GOG", () => Task.Run(RegistryLaunchers.ScanGog));
        if (sources.Contains("eaLocal")) await Run("EA app", () => Task.Run(RegistryLaunchers.ScanEa));
        if (sources.Contains("ubisoftLocal")) await Run("Ubisoft Connect", () => Task.Run(RegistryLaunchers.ScanUbisoft));
        if (sources.Contains("steamOnline") && _s.SteamKey is { } key && _s.SteamId.Length > 0)
        {
            Progress("Leyendo tu biblioteca de Steam…");
            var prog = new Progress<string>(Progress);
            await Run("Steam", () => new SteamClient(_http).GetLibraryAsync(key, _s.SteamId, achievements, prog));
        }
        else if (sources.Contains("steamOnline"))
            messages.Add(new { source = "Steam", ok = false, message = "Para leer tu biblioteca de Steam falta la clave o el SteamID (Ajustes › Cuentas)." });
        if (sources.Contains("xboxOnline") && _s.XblKey is { } xk)
        {
            Progress("Leyendo tu historial de Xbox…");
            await Run("Xbox", () => new OpenXblClient(_http).GetTitleHistoryAsync(xk));
        }
        if (sources.Contains("raOnline") && _s.RaKey is { } rk && _s.RaUser.Length > 0)
        {
            Progress("Leyendo tu perfil de RetroAchievements…");
            await Run("RetroAchievements", () => new RetroAchievementsClient(_http).GetLibraryAsync(_s.RaUser, rk, new Progress<string>(Progress)));
        }
        else if (sources.Contains("raOnline"))
            messages.Add(new { source = "RetroAchievements", ok = false, message = "Para leer RetroAchievements falta tu usuario o la Web API Key (Ajustes › Cuentas)." });
        if (sources.Contains("emuLocal")) { Progress("Buscando juegos de emuladores en este PC…"); await Run("Emuladores", () => Task.Run(() => EmulatorScanner.Scan())); }

        foreach (var r in results.Where(r => !r.Ok || r.Message != null))
            messages.Add(new { source = r.Source, ok = r.Ok, message = r.Message });
        return (ImportPlanner.Merge(results.SelectMany(r => r.Games)), messages);
    }

    private async Task<object> ScanAsync(GameDocument doc, HashSet<string> sources, bool achievements)
    {
        var (games, messages) = await GatherAsync(sources, achievements);
        ImportPlanner.Plan(doc, games, DateTime.Now);
        Directory.CreateDirectory(CoversFolder);
        var list = games.OrderBy(g => g.DuplicateOf != null).ThenByDescending(g => g.LastPlayed ?? DateTime.MinValue).ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(g =>
            {
                string? thumb = null;
                if (g.LocalImage != null && File.Exists(g.LocalImage))
                {
                    var name = "local_" + string.Concat((g.StoreId ?? g.Title).Select(c => char.IsLetterOrDigit(c) ? c : '_')) + Path.GetExtension(g.LocalImage);
                    try { File.Copy(g.LocalImage, Path.Combine(CoversFolder, name), true); thumb = _win.CoverUrl(name); } catch (IOException) { }
                }
                thumb ??= g.CoverUrl;
                return new
                {
                    g.Source, g.Title, g.StoreKey, g.StoreId, g.Hours, g.Achievements, g.LastPlayed, g.Installed, g.Platform, g.CoverUrl,
                    g.Note, g.Hardcore, g.Beaten,
                    g.SuggestedSection, g.SuggestedCategory, g.SuggestedParen, g.DuplicateOf, g.DuplicateEntryId, g.Selected, thumb
                };
            }).ToList();
        if (list.Count == 0 && messages.Count == 0) messages.Add(new { source = "", ok = true, message = "No se ha encontrado ningún juego en las fuentes elegidas." });
        return new { candidates = list, messages };
    }

    private void ApplyImport(GameDocument doc, List<ImportCandidate> items)
    {
        var now = DateTime.Now;
        foreach (var g in items)
        {
            var secName = string.IsNullOrWhiteSpace(g.SuggestedSection) ? ImportPlanner.SectionName(doc, SectionKind.Backlog) : g.SuggestedSection!.Trim();
            var sec = doc.Sections.FirstOrDefault(s => string.Equals(s.Name.Trim(), secName, StringComparison.CurrentCultureIgnoreCase));
            if (sec == null) { sec = new Section { Name = secName }; doc.Sections.Add(sec); }

            Block block;
            if (string.IsNullOrWhiteSpace(g.SuggestedCategory))
            {
                block = sec.Blocks.FirstOrDefault(b => b.Type == BlockType.Category && b.Style == BlockStyle.None)
                        ?? Insert(sec, new Block { Style = BlockStyle.None }, atStart: true);
            }
            else
            {
                var name = g.SuggestedCategory!.Trim();
                block = sec.Blocks.FirstOrDefault(b => b.Type == BlockType.Category && b.Style != BlockStyle.None && SectionKinds.Normalize(b.Name) == SectionKinds.Normalize(name))
                        ?? Insert(sec, new Block { Name = name, Style = g.SuggestedParen ? BlockStyle.Paren : BlockStyle.Colon }, atStart: false);
            }
            g.SuggestedSection = sec.Name;
            block.Entries.Add(ImportPlanner.ToEntry(g, _s.WriteStoreIds, now));
        }
    }

    private static Block Insert(Section s, Block b, bool atStart)
    {
        if (atStart) { s.Blocks.Insert(0, b); return b; }
        var at = s.Blocks.Count;
        while (at > 0 && s.Blocks[at - 1].Type == BlockType.Divider) at--;
        s.Blocks.Insert(at, b);
        return b;
    }

    // ───────────────────────────── IGDB ─────────────────────────────

    private IgdbClient Igdb() =>
        _s.IgdbClientId.Length > 0 && _s.IgdbSecret is { } secret
            ? new IgdbClient(_http, _s.IgdbClientId, secret)
            : throw new UserError("Para buscar duraciones añade el Client ID y el secreto de IGDB en Ajustes › Cuentas.");

    private static object Ttb(IgdbDuration d) => new { d.Id, d.Name, d.Year, d.Platforms, d.Main, d.Full, d.Count, d.Exact, d.Url, d.Field };

    // ───────────────────────────── utilidades ─────────────────────────────

    private static GameDocument Doc(JsonElement a) =>
        a.GetProperty("doc").Deserialize<GameDocument>(Json.Options) ?? throw new UserError("Documento vacío.");
    private static string? Str(JsonElement a, string name) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool Bool(JsonElement a, string name) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

public sealed class UserError(string message) : Exception(message);
