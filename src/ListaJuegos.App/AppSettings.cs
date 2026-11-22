using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ListaJuegos.Core;

namespace ListaJuegos.App;

/// <summary>Ajustes en %APPDATA%\ListaJuegos\settings.json. Las claves de las APIs se cifran con DPAPI (usuario actual).</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "system";
    public string Language { get; set; } = "system";
    public string View { get; set; } = "grid";
    public bool Covers { get; set; }
    public bool CoversAsked { get; set; }
    public bool UpdateDate { get; set; } = true;
    public bool WriteStoreIds { get; set; } = true;
    public bool Backup { get; set; } = true;
    public string SteamId { get; set; } = "";
    public string? SteamKeyProtected { get; set; }
    public string? XblKeyProtected { get; set; }
    public string? SgdbKeyProtected { get; set; }
    public string RaUser { get; set; } = "";
    public string? RaKeyProtected { get; set; }
    /// <summary>Client ID de la aplicación de Twitch para IGDB (no es secreto); el secreto va cifrado.</summary>
    public string IgdbClientId { get; set; } = "";
    /// <summary>Ya se ha visto la bienvenida de la primera vez.</summary>
    public bool Onboarded { get; set; }
    /// <summary>Al abrir sin archivo: «ask» pregunta si continuar con la última lista, «always» la abre, «never» empieza en el inicio.</summary>
    public string Resume { get; set; } = "ask";
    public string? IgdbSecretProtected { get; set; }
    public List<string> Recent { get; set; } = new();
    public MarkdownOptions Markdown { get; set; } = new();
    public double Zoom { get; set; } = 1.0;
    public double[]? Bounds { get; set; }
    public bool Maximized { get; set; }

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ListaJuegos");
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ListaJuegos");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(text, Json.Options) ?? new AppSettings();
                // Si quedó alguna clave en claro de una versión anterior, se reescribe el archivo sin ella.
                using (var doc = JsonDocument.Parse(text))
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && PlainKeyNames.Any(n => doc.RootElement.TryGetProperty(n, out _)))
                        s.Save();
                return s;
            }
        }
        catch (Exception e) when (e is IOException or JsonException) { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json.Options));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (IOException) { }
    }

    // [JsonIgnore]: la clave descifrada nunca se escribe en settings.json (solo la versión *Protected).
    [JsonIgnore] public string? SteamKey { get => Unprotect(SteamKeyProtected); set => SteamKeyProtected = Protect(value); }
    [JsonIgnore] public string? XblKey { get => Unprotect(XblKeyProtected); set => XblKeyProtected = Protect(value); }
    [JsonIgnore] public string? SgdbKey { get => Unprotect(SgdbKeyProtected); set => SgdbKeyProtected = Protect(value); }
    [JsonIgnore] public string? RaKey { get => Unprotect(RaKeyProtected); set => RaKeyProtected = Protect(value); }
    [JsonIgnore] public string? IgdbSecret { get => Unprotect(IgdbSecretProtected); set => IgdbSecretProtected = Protect(value); }

    /// <summary>Nombres con los que versiones anteriores escribían por error la clave descifrada.</summary>
    internal static readonly string[] PlainKeyNames = { "steamKey", "xblKey", "sgdbKey", "raKey", "igdbSecret" };

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ListaJuegos.v1");
#if LINUX
    // Linux: no hay DPAPI. AES-GCM con una clave aleatoria propia del usuario (archivo con permisos 600).
    private static byte[] LinuxKey()
    {
        var f = Path.Combine(Folder, "key");
        if (File.Exists(f)) { var k = File.ReadAllBytes(f); if (k.Length == 32) return k; }
        Directory.CreateDirectory(Folder);
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(f, key);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return key;
    }
    private static string? Protect(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var plain = Encoding.UTF8.GetBytes(s.Trim());
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(LinuxKey(), 16)) aes.Encrypt(nonce, plain, cipher, tag, Entropy);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }
    private static string? Unprotect(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        try
        {
            var all = Convert.FromBase64String(s);
            if (all.Length < 28) return null;
            var plain = new byte[all.Length - 28];
            using (var aes = new AesGcm(LinuxKey(), 16)) aes.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), plain, Entropy);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or IOException) { return null; }
    }
#else
    private static string? Protect(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s.Trim()), Entropy, DataProtectionScope.CurrentUser));
    private static string? Unprotect(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), Entropy, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is CryptographicException or FormatException) { return null; }
    }
#endif

    public void AddRecent(string path)
    {
        Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, path);
        if (Recent.Count > 8) Recent.RemoveRange(8, Recent.Count - 8);
    }

    /// <summary>Lo que ve la interfaz (sin claves).</summary>
    public object Public() => new
    {
        theme = Theme, language = Language, view = View, covers = Covers, coversAsked = CoversAsked, updateDate = UpdateDate, writeStoreIds = WriteStoreIds,
        backup = Backup, steamId = SteamId, hasSteamKey = SteamKeyProtected != null, hasXblKey = XblKeyProtected != null, hasSgdbKey = SgdbKeyProtected != null,
        raUser = RaUser, hasRaKey = RaKeyProtected != null, igdbClientId = IgdbClientId, hasIgdbKey = IgdbSecretProtected != null && IgdbClientId.Length > 0, onboarded = Onboarded, resume = Resume, markdown = Markdown
    };

    public object RecentList() => Recent.Where(File.Exists).Select(p => new { path = p }).ToList();
}
