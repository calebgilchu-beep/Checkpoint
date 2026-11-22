using System.Net;
using System.Text;
using ListaJuegos.Core;
using ListaJuegos.Core.Connectors;
using ListaJuegos.Core.Suggest;

namespace ListaJuegos.Tests;

public class ConnectorTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Urls.Add(r.RequestUri!.ToString());
            return Task.FromResult(f(r));
        }
    }

    private static HttpResponseMessage J(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Steam_biblioteca_y_logros()
    {
        var h = new FakeHandler(r =>
        {
            var u = r.RequestUri!.ToString();
            if (u.Contains("ResolveVanityURL")) return J("""{"response":{"success":1,"steamid":"76561197960287930"}}""");
            if (u.Contains("GetOwnedGames")) return J("""{"response":{"game_count":2,"games":[{"appid":2050650,"name":"Resident Evil 4","playtime_forever":2280,"rtime_last_played":1700000000},{"appid":620,"name":"Portal 2","playtime_forever":0,"rtime_last_played":0}]}}""");
            if (u.Contains("GetPlayerAchievements")) return J("""{"playerstats":{"achievements":[{"achieved":1},{"achieved":1},{"achieved":0},{"achieved":1}]}}""");
            return J("{}", HttpStatusCode.NotFound);
        });
        var s = new SteamClient(new HttpClient(h));
        var res = await s.GetLibraryAsync("KEY", "https://steamcommunity.com/id/gaben/", achievements: true);
        Assert.Equal(2, res.Games.Count);
        var re4 = res.Games[0];
        Assert.Equal("2050650", re4.StoreId);
        Assert.Equal(38, re4.Hours);
        Assert.Equal(75, re4.Achievements);
        Assert.Null(res.Games[1].Achievements); // sin horas no se piden logros
        Assert.Contains(h.Urls, u => u.Contains("vanityurl=gaben"));
    }

    [Fact]
    public async Task Steam_clave_rechazada()
    {
        var s = new SteamClient(new HttpClient(new FakeHandler(_ => J("", HttpStatusCode.Forbidden))));
        var ex = await Assert.ThrowsAsync<ConnectorException>(() => s.GetLibraryAsync("MALA", "76561197960287930", false));
        Assert.Contains("clave", ex.Message);
    }

    [Fact]
    public async Task Steam_busqueda_en_tienda_estricta()
    {
        var s = new SteamClient(new HttpClient(new FakeHandler(_ =>
            J("""{"total":2,"items":[{"id":111,"name":"Super Mario Bros Fan Remake Deluxe Edition 3D"},{"id":1113000,"name":"Persona 4 Golden"}]}"""))));
        Assert.Equal("1113000", (await s.FindAppAsync("Persona 4 Golden #29/10/2026#"))?.appId);
        var none = new SteamClient(new HttpClient(new FakeHandler(_ => J("""{"total":1,"items":[{"id":5,"name":"Mario Kart Clone"}]}"""))));
        Assert.Null(await none.FindAppAsync("Mario Kart 8: Deluxe (50cc, 100cc)"));
    }

    [Fact]
    public async Task OpenXbl_historial()
    {
        var json = """
        {"xuid":"1","titles":[
          {"titleId":"1","name":"Balatro","type":"Game","devices":["PC"],"displayImage":"http://store-images.s-microsoft.com/x.png",
           "achievement":{"currentAchievements":10,"totalAchievements":40,"progressPercentage":25},
           "titleHistory":{"lastTimePlayed":"2026-11-10T20:00:00Z"}},
          {"titleId":"2","name":"Netflix","type":"App"},
          {"titleId":"3","name":"Halo Infinite","type":"Game","devices":["XboxSeries","PC"],"achievement":{"currentAchievements":119,"totalAchievements":119}}
        ]}
        """;
        var h = new FakeHandler(r =>
        {
            Assert.Equal("KEY", r.Headers.GetValues("X-Authorization").Single());
            return J(json);
        });
        var res = await new OpenXblClient(new HttpClient(h)).GetTitleHistoryAsync("KEY");
        Assert.Equal(2, res.Games.Count);
        Assert.Equal("Xbox PC", res.Games[0].Platform);
        Assert.Equal(25, res.Games[0].Achievements);
        Assert.StartsWith("https://", res.Games[0].CoverUrl);
        Assert.Equal("Xbox", res.Games[1].Platform);
        Assert.Equal(100, res.Games[1].Achievements);
    }

    [Fact]
    public void Xbox_local_y_Epic_local()
    {
        var root = Directory.CreateTempSubdirectory("lj_x").FullName;
        try
        {
            var content = Directory.CreateDirectory(Path.Combine(root, "XboxGames", "Balatro", "Content")).FullName;
            File.WriteAllText(Path.Combine(content, "MicrosoftGame.Config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <Game configVersion="1"><Identity Name="PlayStack.Balatro" Publisher="CN=x" Version="1.0.0.0"/>
                <StoreId>9PK087LNGJC5</StoreId><ShellVisuals DefaultDisplayName="Balatro" Square150x150Logo="Logo.png"/></Game>
                """);
            File.WriteAllBytes(Path.Combine(content, "Logo.png"), new byte[2048]);
            var x = XboxLocal.Scan(new[] { Path.Combine(root, "XboxGames") });
            var g = Assert.Single(x.Games);
            Assert.Equal("Balatro", g.Title);
            Assert.Equal("9PK087LNGJC5", g.StoreId);
            Assert.EndsWith("Logo.png", g.LocalImage);

            var man = Directory.CreateDirectory(Path.Combine(root, "Manifests")).FullName;
            File.WriteAllText(Path.Combine(man, "a.item"), """{"DisplayName":"Alan Wake 2","AppName":"Dodo","AppCategories":["public","games","applications"]}""");
            File.WriteAllText(Path.Combine(man, "b.item"), """{"DisplayName":"Unreal Engine","AppName":"UE","AppCategories":["engines"]}""");
            var e = EpicLocal.Scan(man);
            Assert.Equal("Alan Wake 2", Assert.Single(e.Games).Title);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Xbox_local_de_este_pc_si_existe()
    {
        if (!Directory.Exists(@"G:\XboxGames")) return;
        var r = XboxLocal.Scan(new[] { @"G:\XboxGames" });
        Assert.Contains(r.Games, g => g.Title == "Balatro");
    }

    [Fact]
    public void Steam_local_con_vdf()
    {
        var root = Directory.CreateTempSubdirectory("lj_s").FullName;
        try
        {
            var apps = Directory.CreateDirectory(Path.Combine(root, "steamapps")).FullName;
            File.WriteAllText(Path.Combine(apps, "libraryfolders.vdf"), "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + root.Replace(@"\", @"\\") + "\"\n\t}\n}\n");
            File.WriteAllText(Path.Combine(apps, "appmanifest_1145360.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"1145360\"\n\t\"name\"\t\t\"Hades\"\n}\n");
            File.WriteAllText(Path.Combine(apps, "appmanifest_228980.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"228980\"\n\t\"name\"\t\t\"Steamworks Common Redistributables\"\n}\n");
            var cfg = Directory.CreateDirectory(Path.Combine(root, "userdata", "123", "config")).FullName;
            File.WriteAllText(Path.Combine(cfg, "localconfig.vdf"), "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"1145360\" { \"LastPlayed\" \"1700000000\" \"Playtime\" \"3000\" \"cloud\" { \"last_sync_state\" \"synchronized\" } } } } } } }");
            var r = SteamLocal.Scan(root);
            var g = Assert.Single(r.Games);
            Assert.Equal("Hades", g.Title);
            Assert.Equal(50, g.Hours);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Resident Evil 4 (Remake)", "Resident Evil 4", true)]
    [InlineData("Final Fantasy VII", "FINAL FANTASY 7", true)]
    [InlineData("Cuphead + DLC", "Cuphead", true)]
    [InlineData("Mario", "Mario Kart", false)]
    [InlineData("Resident Evil 4", "Resident Evil 5", false)]
    [InlineData("Persona 4 Golden", "Persona 4 Golden Soundtrack", false)]
    [InlineData("Hollow Knight", "Hollow Knight: Silksong", false)]
    public void Coincidencia_de_titulos(string a, string b, bool same) => Assert.Equal(same, TitleMatch.IsSameGame(a, b));

    [Theory]
    [InlineData("Final Fantasy XVI", "Final Fantasy")]
    [InlineData("The Legend of Zelda: Link's Awakening", "The Legend of Zelda")]
    [InlineData("Marvel's Spider-Man 2", "Spider-Man")]
    [InlineData("Metal Gear Solid V: The Phantom Pain", "Metal Gear Solid")]
    [InlineData("Juego Inventado 3000", null)]
    public void Sagas_conocidas(string title, string? saga) => Assert.Equal(saga, Sagas.Find(title));

    [Fact]
    public void Plan_de_importacion()
    {
        var doc = DocumentService.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ejemplo.txt"));
        var now = new DateTime(2026, 11, 21);
        var games = new List<ImportCandidate>
        {
            new() { Source = "steam", Title = "Resident Evil 2", StoreKey = "steam", StoreId = "883710", Hours = 20, Achievements = 100 },
            new() { Source = "steam", Title = "Metal Gear Solid V: The Phantom Pain", Hours = 0 },
            new() { Source = "xbox", Title = "Balatro", LastPlayed = now.AddDays(-2), Hours = 3 },
            new() { Source = "steam", Title = "Undertale", Hours = 9, Genres = { "Indie" } },
            new() { Source = "steam", Title = "Celeste", Hours = 2, Genres = { "Indie", "Acción" } },
            new() { Source = "steam", Title = "Juego Raro", Hours = 1 }
        };
        ImportPlanner.Plan(doc, games, now);

        Assert.Equal("Completados al 100%", games[0].SuggestedSection);
        Assert.Equal("Resident Evil", games[0].SuggestedCategory);
        Assert.Equal("Por jugar", games[1].SuggestedSection);
        Assert.Equal("Metal Gear Solid", games[1].SuggestedCategory);
        Assert.Equal("Por jugar", games[1].DuplicateOf);
        Assert.False(games[1].Selected);
        Assert.Equal("Jugando ahora", games[2].SuggestedSection);
        Assert.Null(games[2].SuggestedCategory);
        Assert.Equal("Completados", games[3].DuplicateOf);
        Assert.Equal("Inclonclusos", games[4].SuggestedSection);
        Assert.Equal(("Indies", true), (games[4].SuggestedCategory, games[4].SuggestedParen));
        Assert.Null(games[5].SuggestedCategory);

        var e = ImportPlanner.ToEntry(games[0], writeStoreId: true, now);
        Assert.Equal(new[] { "20 h", "Logros 100%", "steam:883710" }, e.Fields);
        Assert.Equal("21/11/2026", e.Date);
    }

    [Fact]
    public void Actualizar_datos_enlazados()
    {
        var doc = new GameDocument();
        var s = new Section { Name = "Pasados", Implicit = true };
        var b = new Block { Name = "X" };
        b.Entries.Add(new GameEntry { Title = "Hades", Fields = { "★ 9/10", "10 h", "steam:1145360" } });
        s.Blocks.Add(b); doc.Sections.Add(s);
        var n = ImportPlanner.RefreshLinked(doc, new[] { new ImportCandidate { StoreKey = "steam", StoreId = "1145360", Hours = 61.25, Achievements = 88 } });
        Assert.Equal(1, n);
        Assert.Equal(new[] { "★ 9/10", "61,3 h", "steam:1145360", "Logros 88%" }, b.Entries[0].Fields);
    }

    [Fact]
    public void Merge_de_fuentes()
    {
        var m = ImportPlanner.Merge(new[]
        {
            new ImportCandidate { Source = "steam", Title = "Hades", StoreKey = "steam", StoreId = "1", Installed = true },
            new ImportCandidate { Source = "steam", Title = "Hades", StoreKey = "steam", StoreId = "1", Hours = 5, Achievements = 30 },
            new ImportCandidate { Source = "xbox", Title = "Hades" }
        });
        Assert.Equal(2, m.Count);
        Assert.True(m[0].Installed);
        Assert.Equal(5, m[0].Hours);
    }
    // ── SteamGridDB (respuestas con la forma de la API v2) ──
    private static HttpResponseMessage Img(int size = 4096) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[size]) };

    [Fact]
    public async Task SteamGridDb_busca_por_titulo_y_elige_la_mejor_caratula()
    {
        var h = new FakeHandler(r =>
        {
            Assert.Equal("Bearer", r.Headers.Authorization?.Scheme);
            Assert.Equal("CLAVE", r.Headers.Authorization?.Parameter);
            var u = r.RequestUri!.ToString();
            if (u.Contains("/search/autocomplete/")) return J("""{"success":true,"data":[{"id":1,"name":"Mario Kart 7"},{"id":5248,"name":"Mario Kart 8 Deluxe","verified":true}]}""");
            if (u.Contains("/grids/game/5248") && u.Contains("dimensions=600x900")) return J("""{"success":true,"data":[{"id":10,"score":3,"width":600,"height":900,"nsfw":false,"humor":false,"mime":"image/png","url":"https://cdn2.steamgriddb.com/grid/a.png"},{"id":11,"score":9,"width":600,"height":900,"nsfw":true,"humor":false,"mime":"image/png","url":"https://cdn2.steamgriddb.com/grid/nsfw.png"},{"id":12,"score":7,"width":600,"height":900,"nsfw":false,"humor":false,"mime":"image/webp","url":"https://cdn2.steamgriddb.com/grid/b.webp"}]}""");
            return J("""{"success":false,"errors":["Game not found"]}""", HttpStatusCode.NotFound);
        });
        var c = new SteamGridDbClient(new HttpClient(h), "CLAVE");
        var game = await c.FindGameAsync("Mario Kart 8: Deluxe (50cc, 100cc)");
        Assert.Equal(5248, game?.id);
        var grid = await c.GridForGameAsync(5248);
        Assert.Equal(12, grid?.Id);          // la de más puntuación que no es NSFW
        Assert.Equal(".webp", grid?.Extension);
        Assert.Null(await c.GridForSteamAsync("999"));   // 404: sin portada, sin excepción
    }

    [Fact]
    public async Task SteamGridDb_clave_rechazada()
    {
        var c = new SteamGridDbClient(new HttpClient(new FakeHandler(_ => J("""{"success":false}""", HttpStatusCode.Unauthorized))), "MALA");
        var e = await Assert.ThrowsAsync<ConnectorException>(() => c.CheckKeyAsync());
        Assert.Contains("SteamGridDB", e.Message);
    }

    [Fact]
    public async Task Portadas_con_SteamGridDB_y_respaldo_de_Steam()
    {
        var dir = Directory.CreateTempSubdirectory("lj_sgdb").FullName;
        try
        {
            var h = new FakeHandler(r =>
            {
                var u = r.RequestUri!.ToString();
                if (u.Contains("steamgriddb.com/api/v2/grids/steam/620")) return J("""{"success":true,"data":[{"id":77,"score":1,"width":600,"height":900,"mime":"image/png","url":"https://cdn2.steamgriddb.com/grid/p2.png"}]}""");
                if (u.Contains("steamgriddb.com/api/v2/search/autocomplete/")) return J("""{"success":true,"data":[]}""");
                if (u.Contains("steamgriddb.com/api/v2/")) return J("{}", HttpStatusCode.NotFound);
                if (u.StartsWith("https://cdn2.steamgriddb.com/")) return Img();
                if (u.Contains("storesearch")) return J("""{"total":1,"items":[{"id":367520,"name":"Hollow Knight"}]}""");
                if (u.Contains("library_600x900")) return Img();
                return J("{}", HttpStatusCode.NotFound);
            });
            var http = new HttpClient(h);

            // Sin clave: la tienda de Steam, como antes
            var plain = new CoverService(http, dir);
            Assert.Equal("steam_367520.jpg", await plain.ResolveAsync("Hollow Knight", null, null, null));

            // Con clave: SteamGridDB por appid; si allí no está, se queda la de Steam que ya había
            var covers = new CoverService(http, dir) { SgdbKey = "CLAVE" };
            Assert.Equal("sgdb_77.png", await covers.ResolveAsync("Portal 2", "620", null, null));
            Assert.True(File.Exists(Path.Combine(dir, "sgdb_77.png")));
            Assert.Equal("steam_367520.jpg", await covers.ResolveAsync("Hollow Knight", null, null, null));

            // La segunda vez sale de la caché, sin volver a preguntar a SteamGridDB
            var before = h.Urls.Count;
            Assert.Equal("steam_367520.jpg", await covers.ResolveAsync("Hollow Knight", null, null, null));
            Assert.Equal(before, h.Urls.Count);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task Portada_con_id_de_SteamGridDB_elegido_a_mano()
    {
        var dir = Directory.CreateTempSubdirectory("lj_sgid").FullName;
        try
        {
            var h = new FakeHandler(r =>
            {
                var u = r.RequestUri!.ToString();
                if (u.Contains("/api/v2/grids/game/5245268")) return J("""{"success":true,"data":[{"id":4242,"score":5,"width":600,"height":900,"mime":"image/jpeg","url":"https://cdn2.steamgriddb.com/grid/smo.jpg"}]}""");
                if (u.StartsWith("https://cdn2.steamgriddb.com/")) return Img();
                return J("{}", HttpStatusCode.NotFound);
            });
            var covers = new CoverService(new HttpClient(h), dir) { SgdbKey = "CLAVE" };
            Assert.Equal("sgdb_4242.jpg", await covers.ResolveAsync("Super Mario Odyssey", null, null, null, "5245268"));
            Assert.DoesNotContain(h.Urls, u => u.Contains("autocomplete") || u.Contains("storesearch"));   // no busca por nombre
        }
        finally { Directory.Delete(dir, true); }
    }
}
