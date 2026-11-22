using System.Text.Json;
using Microsoft.Win32;

namespace ListaJuegos.Core.Connectors;

/// <summary>Epic Games: manifiestos locales del lanzador (Epic no tiene API pública de biblioteca).</summary>
public static class EpicLocal
{
    public static string DefaultManifests =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public static ConnectorResult Scan(string? dir = null)
    {
        dir ??= DefaultManifests;
        var res = new ConnectorResult { Source = "epic" };
        if (!Directory.Exists(dir)) { res.Ok = false; res.Message = "Epic Games Launcher no está instalado en este PC."; return res; }
        foreach (var f in Directory.EnumerateFiles(dir, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var r = doc.RootElement;
                var name = r.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
                var app = r.TryGetProperty("AppName", out var a) ? a.GetString() : null;
                if (r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) continue;
                if (r.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array
                    && !cats.EnumerateArray().Any(c => c.GetString() == "games")) continue;
                if (string.IsNullOrWhiteSpace(name)) continue;
                res.Games.Add(new ImportCandidate
                {
                    Source = "epic", Title = name!, StoreKey = app != null ? "epic" : null, StoreId = app,
                    Installed = true, Platform = "Epic Games"
                });
            }
            catch (Exception e) when (e is IOException or JsonException) { }
        }
        return res;
    }
}

/// <summary>GOG Galaxy, EA app y Ubisoft Connect a partir del registro de Windows.</summary>
public static class RegistryLaunchers
{
    public static ConnectorResult ScanGog()
    {
        var res = new ConnectorResult { Source = "gog" };
        if (!OperatingSystem.IsWindows()) { res.Ok = false; res.Message = "GOG Galaxy solo existe en Windows."; return res; }
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (k == null) { res.Ok = false; res.Message = "GOG Galaxy no tiene juegos instalados en este PC."; return res; }
        foreach (var sub in k.GetSubKeyNames())
        {
            using var g = k.OpenSubKey(sub);
            var name = g?.GetValue("gameName") as string;
            if (string.IsNullOrWhiteSpace(name)) continue;
            res.Games.Add(new ImportCandidate { Source = "gog", Title = name, StoreKey = "gog", StoreId = sub, Installed = true, Platform = "GOG" });
        }
        return res;
    }

    public static ConnectorResult ScanUninstall(string source, string publisherContains, string platform)
    {
        var res = new ConnectorResult { Source = source };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) { res.Ok = false; res.Message = $"{platform} solo existe en Windows."; return res; }
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using var k = hive.OpenSubKey(path);
                if (k == null) continue;
                foreach (var sub in k.GetSubKeyNames())
                {
                    using var g = k.OpenSubKey(sub);
                    var pub = g?.GetValue("Publisher") as string ?? "";
                    var name = g?.GetValue("DisplayName") as string;
                    if (name == null || !pub.Contains(publisherContains, StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Contains("EA app", StringComparison.OrdinalIgnoreCase) || name.Contains("Ubisoft Connect", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Origin", StringComparison.OrdinalIgnoreCase) && name.Length < 10) continue;
                    if (!seen.Add(name)) continue;
                    res.Games.Add(new ImportCandidate { Source = source, Title = name, Installed = true, Platform = platform });
                }
            }
        if (res.Games.Count == 0) res.Message = $"No se han encontrado juegos de {platform} instalados.";
        return res;
    }

    public static ConnectorResult ScanEa() => ScanUninstall("ea", "Electronic Arts", "EA app");
    public static ConnectorResult ScanUbisoft() => ScanUninstall("ubisoft", "Ubisoft", "Ubisoft Connect");
}
