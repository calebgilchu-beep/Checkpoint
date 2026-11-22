using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using ListaJuegos.Core;
using ListaJuegos.Core.Connectors;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace ListaJuegos.App;

public partial class MainWindow : Window
{
    public const string AppHost = "app.lj";
    public const string CoversHost = "covers.lj";

    private readonly AppSettings _settings;
    private readonly Bridge _bridge;
    private readonly Dictionary<string, string> _resources;
    private bool _dirty, _forceClose;
    private string _docTitle = "Checkpoint";

    private static readonly HttpClient Http = CreateHttp();

    public MainWindow(string? startupFile)
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        Lang.Set(_settings.Language);
        _bridge = new Bridge(this, _settings, Http) { StartupFile = startupFile };
        _resources = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.StartsWith("ui/", StringComparison.Ordinal) || n.StartsWith("ui\\", StringComparison.Ordinal))
            .ToDictionary(n => n.Replace('\\', '/')[3..], n => n, StringComparer.OrdinalIgnoreCase);

        RestoreWindowBounds();
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(ApplyTitleBarTheme);
        Loaded += async (_, _) => await InitWebAsync();
        Closing += OnClosing;
    }

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("ListaJuegos/1.0 (Windows)");
        return h;
    }

    public double Zoom
    {
        get => Web.ZoomFactor;
        set => Web.ZoomFactor = value;
    }

    private async Task InitWebAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppSettings.DataFolder, "WebView2"));
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or COMException)
        {
            Web.Visibility = Visibility.Collapsed;
            Fallback.Visibility = Visibility.Visible;
            Fallback.Text = Lang.T(
                "Checkpoint necesita el componente Microsoft Edge WebView2, que viene con Windows 11.\nInstálalo desde https://go.microsoft.com/fwlink/p/?LinkId=2124703 y vuelve a abrir la app.",
                "Checkpoint needs the Microsoft Edge WebView2 component, included with Windows 11.\nInstall it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and reopen the app.",
                "O Checkpoint precisa do componente Microsoft Edge WebView2, incluído no Windows 11.\nInstale-o em https://go.microsoft.com/fwlink/p/?LinkId=2124703 e abra o app de novo.") + "\n\n" + e.Message;
            return;
        }

        var core = Web.CoreWebView2;
        var st = core.Settings;
        st.AreBrowserAcceleratorKeysEnabled = false;
        st.IsZoomControlEnabled = false;
        st.IsStatusBarEnabled = false;
        st.IsGeneralAutofillEnabled = false;
        st.IsPasswordAutosaveEnabled = false;
        st.AreDevToolsEnabled = Environment.GetEnvironmentVariable("LJ_DEVTOOLS") == "1";

        Directory.CreateDirectory(Bridge.CoversFolder);
        core.SetVirtualHostNameToFolderMapping(CoversHost, Bridge.CoversFolder, CoreWebView2HostResourceAccessKind.Allow);
        core.AddWebResourceRequestedFilter($"https://{AppHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += ServeResource;
        core.WebMessageReceived += OnMessage;
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith($"https://{AppHost}/", StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            OpenExternal(e.Uri);
        };
        core.NewWindowRequested += (_, e) => { e.Handled = true; OpenExternal(e.Uri); };
        core.ContextMenuRequested += (_, e) =>
        {
            // Solo las órdenes de edición de texto (cortar, copiar, pegar…), sin «Inspeccionar» ni «Recargar».
            var keep = new HashSet<string> { "cut", "copy", "paste", "pasteAndMatchStyle", "selectAll", "undo", "redo", "emoji", "spellCheck" };
            foreach (var item in e.MenuItems.ToList())
                if (!keep.Contains(item.Name)) e.MenuItems.Remove(item);
            if (e.MenuItems.Count == 0) e.Handled = true;
        };
        Web.ZoomFactor = _settings.Zoom is > 0.5 and < 2.1 ? _settings.Zoom : 1.0;
        core.Navigate($"https://{AppHost}/index.html");
    }

    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".css"] = "text/css; charset=utf-8", [".js"] = "text/javascript; charset=utf-8",
        [".svg"] = "image/svg+xml", [".woff2"] = "font/woff2", [".json"] = "application/json", [".png"] = "image/png", [".ico"] = "image/x-icon",
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".webp"] = "image/webp"
    };

    private void ServeResource(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (path.Length == 0) path = "index.html";
        // Portadas ya descargadas, servidas desde el mismo origen que la interfaz para poder dibujarlas en un <canvas>
        // («Resumen del año») sin que el navegador lo marque como contaminado. Solo archivos de la carpeta de portadas.
        if (path.StartsWith("cache/covers/", StringComparison.OrdinalIgnoreCase))
        {
            var name = path["cache/covers/".Length..];
            var file = Path.Combine(Bridge.CoversFolder, name);
            if (name.Length > 0 && Path.GetFileName(name) == name && File.Exists(file))
            {
                e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(File.OpenRead(file), 200, "OK",
                    $"Content-Type: {Mime.GetValueOrDefault(Path.GetExtension(file), "application/octet-stream")}\r\nCache-Control: no-cache");
            }
            else e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
            return;
        }
        if (!_resources.TryGetValue(path, out var res))
        {
            e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
            return;
        }
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(res)!;
        var mime = Mime.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
        e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(stream, 200, "OK",
            $"Content-Type: {mime}\r\nCache-Control: no-cache\r\n" +
            "Content-Security-Policy: default-src 'self'; img-src 'self' https: data:; style-src 'self' 'unsafe-inline'; script-src 'self'; font-src 'self'; connect-src 'self'");
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.TryGetWebMessageAsString(); } catch (ArgumentException) { json = e.WebMessageAsJson; }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { return; }
        using var _ = doc;
        var root = doc.RootElement;
        var cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";

        if (cmd == "drop")
        {
            var file = e.AdditionalObjects?.OfType<CoreWebView2File>()
                .Select(f => f.Path)
                .FirstOrDefault(p => p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                                     || p.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase));
            if (file != null) PostEvent("fileDropped", new { path = file });
            return;
        }

        var id = root.GetProperty("id").GetInt32();
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
                UnauthorizedAccessException => "Windows no deja acceder a ese archivo o carpeta.",
                HttpRequestException => "No hay conexión con el servicio. Comprueba Internet.",
                TaskCanceledException => "El servicio ha tardado demasiado en responder.",
                _ => ex.Message
            };
            Post(new { reply = id, ok = false, error = msg });
        }
    }

    private void Post(object payload)
    {
        var json = JsonSerializer.Serialize(payload, Json.Options);
        if (Dispatcher.CheckAccess()) Web.CoreWebView2?.PostWebMessageAsJson(json);
        else Dispatcher.BeginInvoke(() => Web.CoreWebView2?.PostWebMessageAsJson(json));
    }

    public void PostEvent(string name, object data) => Post(new { @event = name, data });

    private static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
    }

    // ───────────────────────────── ventana ─────────────────────────────

    public void SetDirty(bool dirty, string title)
    {
        _dirty = dirty;
        _docTitle = title;
        Title = (dirty ? "• " : "") + title + " — Checkpoint";
    }

    // Pantalla completa sin bordes (F11): sin barra de título ni marco, encima de la barra de tareas.
    private bool _full;
    private WindowState _prevState;
    private ResizeMode _prevResize;

    public bool FullScreen => _full;

    public bool SetFullScreen(bool on)
    {
        if (on == _full) return _full;
        if (on)
        {
            _prevState = WindowState;
            _prevResize = ResizeMode;
            // Hay que pasar por Normal: si ya está maximizada, WPF no recalcula el área y deja ver la barra de tareas.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = _prevResize;
            WindowState = _prevState;
        }
        _full = on;
        PostEvent("fullScreen", new { on });
        return _full;
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_dirty && !_forceClose)
        {
            var r = MessageBox.Show(this, Lang.T($"Hay cambios sin guardar en «{_docTitle}».\n\n¿Quieres guardarlos antes de salir?",
                $"There are unsaved changes in “{_docTitle}”.\n\nDo you want to save them before closing?",
                $"Há alterações não salvas em “{_docTitle}”.\n\nDeseja salvá-las antes de sair?"),
                "Checkpoint", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.Yes) { e.Cancel = true; PostEvent("saveAndClose", new { }); return; }
        }
        SetFullScreen(false);   // se guarda el tamaño y la posición de la ventana normal
        SaveWindowBounds();
    }

    private void RestoreWindowBounds()
    {
        var b = _settings.Bounds;
        if (b is { Length: 4 } && b[2] >= MinWidth && b[3] >= MinHeight)
        {
            var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (vs.IntersectsWith(new Rect(b[0], b[1], b[2], b[3])))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = b[0]; Top = b[1]; Width = b[2]; Height = b[3];
            }
        }
        if (_settings.Maximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindowBounds()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Bounds = new[] { r.Left, r.Top, r.Width, r.Height };
        _settings.Maximized = WindowState == WindowState.Maximized;
        _settings.Save();
    }

    // Barra de título oscura o clara, del color de fondo de la app (Windows 11).
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public void ApplyTitleBarTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = _settings.Theme switch
        {
            "dark" => true,
            "light" => false,
            _ => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int v && v == 0
        };
        int on = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));        // DWMWA_USE_IMMERSIVE_DARK_MODE
        int caption = dark ? 0x000B060D : 0x00F6F4F7;               // COLORREF 0x00BBGGRR
        int text = dark ? 0x00FFFFFF : 0x000B060D;
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));   // DWMWA_CAPTION_COLOR
        DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));      // DWMWA_TEXT_COLOR
        var bg = dark ? System.Windows.Media.Color.FromRgb(0x0D, 0x06, 0x0B) : System.Windows.Media.Color.FromRgb(0xF7, 0xF4, 0xF6);
        Background = new System.Windows.Media.SolidColorBrush(bg);
        if (Web.CoreWebView2 == null) Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(bg.R, bg.G, bg.B);
    }
}
