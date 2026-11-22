using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ListaJuegos.Core;
using ListaJuegos.Core.Connectors;
using Photino.NET;

namespace ListaJuegos.App;

/// <summary>Ventana de Linux (Photino + WebKitGTK). Hace lo mismo que MainWindow en Windows.</summary>
public sealed class LinuxWindow : IHostUi
{
    public const string Origin = "app://localhost/";

    private static readonly HttpClient Http = CreateHttp();
    private readonly PhotinoWindow _w;
    private readonly AppSettings _settings;
    private readonly Bridge _bridge;
    private readonly Dictionary<string, string> _resources;
    private bool _dirty, _forceClose;
    private string _docTitle = "Checkpoint";

    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html", [".css"] = "text/css", [".js"] = "text/javascript",
        [".svg"] = "image/svg+xml", [".woff2"] = "font/woff2", [".json"] = "application/json", [".png"] = "image/png", [".ico"] = "image/x-icon",
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".webp"] = "image/webp"
    };

    public LinuxWindow(string? startupFile)
    {
        _settings = AppSettings.Load();
        Lang.Set(_settings.Language);
        _bridge = new Bridge(this, _settings, Http) { StartupFile = startupFile };
        _resources = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.StartsWith("ui/", StringComparison.Ordinal) || n.StartsWith("ui\\", StringComparison.Ordinal))
            .ToDictionary(n => n.Replace('\\', '/')[3..], n => n, StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(Bridge.CoversFolder);

        _w = new PhotinoWindow()
            .SetTitle("Checkpoint")
            .SetUseOsDefaultSize(false)
            .SetSize(1360, 860)
            .SetMinSize(900, 600)
            .SetDevToolsEnabled(Environment.GetEnvironmentVariable("LJ_DEVTOOLS") == "1")
            .SetContextMenuEnabled(Environment.GetEnvironmentVariable("LJ_DEVTOOLS") == "1")
            .SetZoom(_settings.Zoom is > 0.5 and < 2.1 ? (int)Math.Round(_settings.Zoom * 100) : 100)
            .RegisterCustomSchemeHandler("app", Serve)
            .RegisterWebMessageReceivedHandler(OnMessage)
            .RegisterWindowClosingHandler(OnClosing);

        var icon = FindIcon();
        if (icon != null) _w.SetIconFile(icon);

        var b = _settings.Bounds;
        if (b is { Length: 4 } && b[2] >= 900 && b[3] >= 600) _w.SetSize((int)b[2], (int)b[3]).SetLeft((int)b[0]).SetTop((int)b[1]);
        else _w.Center();
        if (_settings.Maximized) _w.SetMaximized(true);
    }

    public void Run()
    {
        _w.Load(new Uri(Origin + "index.html"));
        _w.WaitForClose();
    }

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("ListaJuegos/1.0 (Linux)");
        return h;
    }

    private static string? FindIcon()
    {
        foreach (var d in new[] { Environment.GetEnvironmentVariable("APPDIR"), AppContext.BaseDirectory })
            if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, "checkpoint.png"))) return Path.Combine(d, "checkpoint.png");
        return null;
    }

    // ───────────────────────────── recursos ─────────────────────────────

    private Stream Serve(object sender, string scheme, string url, out string contentType)
    {
        contentType = "text/plain";
        var path = Uri.TryCreate(url, UriKind.Absolute, out var u) ? Uri.UnescapeDataString(u.AbsolutePath.TrimStart('/')) : "";
        if (path.Length == 0) path = "index.html";
        if (path.StartsWith("cache/covers/", StringComparison.OrdinalIgnoreCase))
        {
            // Portadas ya descargadas, desde el mismo origen que la interfaz (para el <canvas> del «Resumen del año»).
            var name = path["cache/covers/".Length..];
            var file = Path.Combine(Bridge.CoversFolder, name);
            if (name.Length > 0 && Path.GetFileName(name) == name && File.Exists(file))
            {
                contentType = Mime.GetValueOrDefault(Path.GetExtension(file), "application/octet-stream");
                return new MemoryStream(File.ReadAllBytes(file));
            }
            return new MemoryStream();
        }
        if (!_resources.TryGetValue(path, out var res)) return new MemoryStream();
        contentType = Mime.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(res)!;
    }

    // ───────────────────────────── mensajes ─────────────────────────────

    private void Post(object payload)
    {
        var json = JsonSerializer.Serialize(payload, Json.Options);
        _w.Invoke(() => _w.SendWebMessage(json));
    }

    public void PostEvent(string name, object data) => Post(new { @event = name, data });

    private async void OnMessage(object? sender, string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { return; }
        using var _ = doc;
        var root = doc.RootElement;
        var cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";
        if (!root.TryGetProperty("id", out var idEl)) return;
        var id = idEl.GetInt32();
        var args = root.TryGetProperty("args", out var a) ? a.Clone() : default;
        try
        {
            var result = await _bridge.HandleAsync(cmd, args);
            Post(new { reply = id, ok = true, result });
        }
        catch (Exception ex)
        {
            var msg = ex switch
            {
                UserError or ConnectorException => ex.Message,
                IOException io => $"No se pudo acceder al archivo: {io.Message}",
                UnauthorizedAccessException => "El sistema no deja acceder a ese archivo o carpeta.",
                HttpRequestException => "No hay conexión con el servicio. Comprueba Internet.",
                TaskCanceledException => "El servicio ha tardado demasiado en responder.",
                _ => ex.Message
            };
            Post(new { reply = id, ok = false, error = msg });
        }
    }

    // ───────────────────────────── IHostUi ─────────────────────────────

    private static (string Name, string[] Extensions)[] Filters(FileFilter[] f) =>
        f.Select(x => (x.Name, x.Exts.Select(e => e == "*" ? "*" : "*." + e).ToArray())).ToArray();

    public string? PickOpen(string title, FileFilter[] filters, string initialDir)
    {
        var r = _w.ShowOpenFile(title, initialDir, false, Filters(filters));
        return r is { Length: > 0 } && !string.IsNullOrEmpty(r[0]) ? r[0] : null;
    }

    public string? PickSave(string title, FileFilter[] filters, int filterIndex, string defaultExt, string fileName, string initialDir)
    {
        // El filtro elegido va primero: GTK lo toma como el activo.
        var ordered = filters.Skip(filterIndex - 1).Take(1).Concat(filters.Where((_, i) => i != filterIndex - 1)).ToArray();
        var r = _w.ShowSaveFile(title, Path.Combine(initialDir, fileName), Filters(ordered));
        if (string.IsNullOrEmpty(r)) return null;
        return Path.HasExtension(r) ? r : r + defaultExt;
    }

    public void SetDirty(bool dirty, string title)
    {
        _dirty = dirty;
        _docTitle = title;
        _w.Invoke(() => _w.SetTitle((dirty ? "• " : "") + title + " — Checkpoint"));
    }

    public void ApplyTitleBarTheme() { }   // la barra de título la pinta el tema de GTK

    public double Zoom
    {
        get => _w.Zoom / 100.0;
        set => _w.Invoke(() => _w.SetZoom((int)Math.Round(value * 100)));
    }

    public bool FullScreen => _w.FullScreen;

    public bool SetFullScreen(bool on)
    {
        if (on != _w.FullScreen) _w.Invoke(() => _w.SetFullScreen(on));
        PostEvent("fullScreen", new { on });
        return on;
    }

    public void ForceClose()
    {
        _forceClose = true;
        _w.Invoke(() => _w.Close());
    }

    public string CoverUrl(string file) => Origin + "cache/covers/" + Uri.EscapeDataString(file);

    public void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private bool OnClosing(object sender, EventArgs e)
    {
        if (_dirty && !_forceClose)
        {
            var r = _w.ShowMessage("Checkpoint", Lang.T($"Hay cambios sin guardar en «{_docTitle}».\n\n¿Quieres guardarlos antes de salir?",
                $"There are unsaved changes in “{_docTitle}”.\n\nDo you want to save them before closing?",
                $"Há alterações não salvas em “{_docTitle}”.\n\nDeseja salvá-las antes de sair?"),
                PhotinoDialogButtons.YesNoCancel, PhotinoDialogIcon.Question);
            if (r == PhotinoDialogResult.Cancel) return true;
            if (r == PhotinoDialogResult.Yes) { PostEvent("saveAndClose", new { }); return true; }
        }
        if (!_w.FullScreen && !_w.Maximized)
            _settings.Bounds = new double[] { _w.Left, _w.Top, _w.Width, _w.Height };
        _settings.Maximized = _w.Maximized;
        _settings.Save();
        return false;
    }
}
