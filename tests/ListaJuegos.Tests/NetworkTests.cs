using ListaJuegos.Core.Connectors;

namespace ListaJuegos.Tests;

/// <summary>Pruebas contra la tienda real de Steam (sin clave). Solo se ejecutan con LJ_NET=1.</summary>
public class NetworkTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("LJ_NET") == "1";

    [Fact]
    public async Task Portadas_reales_de_Steam()
    {
        if (!Enabled) return;
        var dir = Directory.CreateTempSubdirectory("lj_cov").FullName;
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ListaJuegos/1.0 (Windows)");
            var covers = new CoverService(http, dir);
            var portal = await covers.ResolveAsync("Portal 2", null, null, null);
            Assert.NotNull(portal);
            Assert.True(new FileInfo(Path.Combine(dir, portal!)).Length > 10_000);
            Assert.Equal("620", covers.KnownAppId("Portal 2"));

            var hk = await covers.ResolveAsync("Hollow Knight", null, null, null);
            Assert.NotNull(hk);

            // Un juego de Nintendo no está en Steam: sin portada (y se muestra el nombre).
            Assert.Null(await covers.ResolveAsync("Mario Kart 8: Deluxe (50cc, 100cc)", null, null, null));

            var genres = await new SteamClient(http).GetGenresAsync("367520");
            Assert.Contains(genres, g => g.Contains("Indie", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir, true); }
    }
}
