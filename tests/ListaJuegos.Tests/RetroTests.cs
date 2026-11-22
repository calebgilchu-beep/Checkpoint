using System.Net;
using System.Text;
using ListaJuegos.Core;
using ListaJuegos.Core.Connectors;
using ListaJuegos.Core.Parsing;
using ListaJuegos.Core.Suggest;

namespace ListaJuegos.Tests;

/// <summary>Fase 1: RetroAchievements, emuladores locales y limpieza de nombres de ROM.</summary>
public class RetroTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "lj-retro-" + Guid.NewGuid().ToString("N")[..8]);
    public RetroTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

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

    private string Write(string rel, string text)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
    }

    // ───────────────────────────── RetroAchievements ─────────────────────────────

    private const string Progress = """
        {"Count":3,"Total":3,"Results":[
          {"GameID":228,"Title":"Super Mario World","ImageIcon":"/Images/066152.png","ConsoleID":3,"ConsoleName":"SNES/Super Famicom",
           "MaxPossible":96,"NumAwarded":96,"NumAwardedHardcore":96,"MostRecentAwardedDate":"2026-06-01T20:00:00+00:00","HighestAwardKind":"mastered"},
          {"GameID":1,"Title":"Legend of Zelda, The: A Link to the Past","ConsoleName":"SNES/Super Famicom",
           "MaxPossible":"100","NumAwarded":"42","NumAwardedHardcore":"30","MostRecentAwardedDate":"2026-01-10T10:00:00+00:00","HighestAwardKind":null},
          {"GameID":11279,"Title":"Crash Bandicoot","ConsoleName":"PlayStation",
           "MaxPossible":40,"NumAwarded":12,"NumAwardedHardcore":0,"HighestAwardKind":"beaten-softcore"}]}
        """;

    private const string Recent = """
        [{"GameID":1,"ConsoleID":3,"ConsoleName":"SNES/Super Famicom","Title":"Legend of Zelda, The: A Link to the Past","LastPlayed":"2026-11-18 21:30:00",
          "NumPossibleAchievements":100,"NumAchieved":45,"NumAchievedHardcore":30},
         {"GameID":5432,"ConsoleID":5,"ConsoleName":"Game Boy Advance","Title":"Metroid Fusion","LastPlayed":"2026-11-15 18:00:00",
          "NumPossibleAchievements":0,"NumAchieved":0,"NumAchievedHardcore":0}]
        """;

    [Fact]
    public async Task RA_lee_progreso_hardcore_y_ultima_sesion()
    {
        var h = new FakeHandler(r =>
        {
            var u = r.RequestUri!.ToString();
            if (u.Contains("API_GetUserCompletionProgress")) return J(Progress);
            if (u.Contains("API_GetUserRecentlyPlayedGames")) return J(Recent);
            return J("{}", HttpStatusCode.NotFound);
        });
        var res = await new RetroAchievementsClient(new HttpClient(h)).GetLibraryAsync(" Caleb ", "CLAVE123");

        Assert.Equal(4, res.Games.Count);
        Assert.All(res.Games, g => { Assert.Equal("ra", g.StoreKey); Assert.Equal("emulación", g.Note); });

        var smw = res.Games.Single(g => g.StoreId == "228");
        Assert.Equal("Super Mario World", smw.Title);
        Assert.Equal("SNES", smw.Platform);
        Assert.Equal(100, smw.Achievements);
        Assert.Equal(100, smw.Hardcore);
        Assert.Equal("https://media.retroachievements.org/Images/066152.png", smw.CoverUrl);

        var zelda = res.Games.Single(g => g.StoreId == "1");
        Assert.Equal("The Legend of Zelda: A Link to the Past", zelda.Title);   // «, The» delante; números en texto
        Assert.Equal(45, zelda.Achievements);                                    // lo más alto de las dos lecturas
        Assert.Equal(30, zelda.Hardcore);
        Assert.Equal(new DateTime(2026, 11, 18), zelda.LastPlayed!.Value.ToUniversalTime().Date);

        var crash = res.Games.Single(g => g.StoreId == "11279");
        Assert.Equal("PS1", crash.Platform);
        Assert.True(crash.Beaten);
        Assert.Null(crash.Hardcore);                                             // 0 en Hardcore: no se anota

        var fusion = res.Games.Single(g => g.StoreId == "5432");
        Assert.Equal("GBA", fusion.Platform);
        Assert.Null(fusion.Achievements);                                        // juego sin logros

        Assert.Contains(h.Urls, u => u.Contains("u=Caleb&y=CLAVE123"));
    }

    [Fact]
    public async Task RA_pagina_la_biblioteca()
    {
        var calls = 0;
        var h = new FakeHandler(r =>
        {
            var u = r.RequestUri!.ToString();
            if (u.Contains("API_GetUserRecentlyPlayedGames")) return J("[]");
            calls++;
            var offset = int.Parse(System.Text.RegularExpressions.Regex.Match(u, @"[?&]o=(\d+)").Groups[1].Value);
            var n = offset == 0 ? 500 : 100;
            var items = Enumerable.Range(offset, n).Select(i => $$"""{"GameID":{{i + 1}},"Title":"Juego {{i + 1}}","ConsoleName":"NES/Famicom","MaxPossible":10,"NumAwarded":1}""");
            return J($$"""{"Count":{{n}},"Total":600,"Results":[{{string.Join(",", items)}}]}""");
        });
        var res = await new RetroAchievementsClient(new HttpClient(h)).GetLibraryAsync("u", "k");
        Assert.Equal(2, calls);
        Assert.Equal(600, res.Games.Count);
        Assert.All(res.Games, g => Assert.Equal("NES", g.Platform));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "rechazado")]
    [InlineData(HttpStatusCode.NotFound, "usuario")]
    public async Task RA_errores_en_espanol(HttpStatusCode code, string expected)
    {
        var c = new RetroAchievementsClient(new HttpClient(new FakeHandler(_ => J("", code))));
        var ex = await Assert.ThrowsAsync<ConnectorException>(() => c.GetLibraryAsync("u", "CLAVESECRETA"));
        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("CLAVESECRETA", ex.Message);       // la clave nunca aparece en el mensaje
    }

    [Fact]
    public async Task RA_clave_no_valida_con_200()
    {
        var c = new RetroAchievementsClient(new HttpClient(new FakeHandler(_ => J("""{"message":"Unauthenticated.","errors":[{"status":401}]}"""))));
        var ex = await Assert.ThrowsAsync<ConnectorException>(() => c.GetLibraryAsync("u", "k"));
        Assert.Contains("rechazado", ex.Message);
    }

    [Fact]
    public async Task RA_sin_usuario()
    {
        var c = new RetroAchievementsClient(new HttpClient(new FakeHandler(_ => J("[]"))));
        await Assert.ThrowsAsync<ConnectorException>(() => c.GetLibraryAsync("  ", "k"));
    }

    [Fact]
    public void Clasificacion_de_juegos_retro()
    {
        var now = new DateTime(2026, 11, 20);
        Assert.Equal(SectionKind.Hundred, ImportPlanner.SuggestKind(new ImportCandidate { Achievements = 100, LastPlayed = now.AddDays(-200) }, now));
        Assert.Equal(SectionKind.Playing, ImportPlanner.SuggestKind(new ImportCandidate { Achievements = 40, LastPlayed = now.AddDays(-2) }, now));
        Assert.Equal(SectionKind.Unfinished, ImportPlanner.SuggestKind(new ImportCandidate { Achievements = 40, LastPlayed = now.AddDays(-90) }, now));
        Assert.Equal(SectionKind.Completed, ImportPlanner.SuggestKind(new ImportCandidate { Achievements = 30, Beaten = true }, now));
        Assert.Equal(SectionKind.Backlog, ImportPlanner.SuggestKind(new ImportCandidate { Achievements = 0 }, now));
    }

    [Fact]
    public void Entrada_importada_con_nota_hardcore_e_id()
    {
        var g = new ImportCandidate { Title = "Super Mario World", Platform = "SNES", Note = "emulación", Achievements = 100, Hardcore = 100, StoreKey = "ra", StoreId = "228", SuggestedSection = "Completados al 100%" };
        var e = ImportPlanner.ToEntry(g, writeStoreId: true, new DateTime(2026, 11, 20));
        var line = EntryLine.Format(e);
        Assert.Equal("Super Mario World [emulación] - SNES #20/11/2026# {Logros 100%} {Hardcore: 100%} {ra:228}", line);

        var back = EntryLine.Parse(line);
        Assert.Equal("emulación", back.Note);
        Assert.Equal(NotePosition.BeforePlatform, back.NotePos);
        Assert.Equal("228", Fields.StoreId(back, "ra"));
        Assert.Equal(FieldKind.StoreId, Fields.Classify("ra:228"));
    }

    [Fact]
    public void Actualizar_enlazados_con_ra_aunque_tenga_otra_tienda()
    {
        var doc = DocumentService.Parse("Lista\r\n\r\n-Sonic Mania - PC {Logros 10%} {steam:584400} {ra:9999}\r\n-Zelda [emulación] - SNES {Logros 20%} {Hardcore: 5%} {ra:1}\r\n", FileKind.Txt);
        var changed = ImportPlanner.RefreshLinked(doc, new[]
        {
            new ImportCandidate { StoreKey = "ra", StoreId = "1", Achievements = 45, Hardcore = 30 },
            new ImportCandidate { StoreKey = "ra", StoreId = "9999", Achievements = 50 }
        });
        Assert.Equal(2, changed);
        var es = doc.Sections.SelectMany(s => s.Entries).ToList();
        Assert.Equal(new[] { "Logros 50%", "steam:584400", "ra:9999" }, es[0].Fields);
        Assert.Equal(new[] { "Logros 45%", "Hardcore: 30%", "ra:1" }, es[1].Fields);
    }

    [Fact]
    public void Enlace_de_RetroAchievements_en_md_ida_y_vuelta()
    {
        Assert.Equal("https://retroachievements.org/game/228", Fields.StoreUrl("ra", "228"));
        Assert.Equal(("ra", "228"), Fields.FromStoreUrl("https://retroachievements.org/game/228"));
        var doc = DocumentService.Parse("Lista\r\n\r\n-Super Mario World [emulación] - SNES {Logros 100%} {ra:228}\r\n", FileKind.Txt);
        var md = DocumentService.Serialize(doc, FileKind.Md);
        Assert.Contains("(https://retroachievements.org/game/228)", md);
        var back = DocumentService.Parse(md, FileKind.Md).Sections.SelectMany(s => s.Entries).Single();
        Assert.Equal("228", Fields.StoreId(back, "ra"));
        Assert.Equal("emulación", back.Note);
    }

    [Fact]
    public void Lineas_existentes_con_ra_se_guardan_igual()
    {
        const string txt = "Lista (Actualizado el 01/01/2026)\r\n\r\n-Super Mario World [emulación] - SNES  {ra:228}\r\n-Otro juego - GBA {ra: 12}\r\n";
        var doc = DocumentService.Parse(txt, FileKind.Txt);
        Assert.Equal(txt, DocumentService.Serialize(doc, FileKind.Txt));
    }

    // ───────────────────────────── nombres de ROM y consolas ─────────────────────────────

    [Theory]
    [InlineData("Super Mario World (USA)", "Super Mario World")]
    [InlineData("Super Mario Bros. 3 (USA) (Rev 1)", "Super Mario Bros. 3")]
    [InlineData("Legend of Zelda, The - A Link to the Past (USA) [!]", "The Legend of Zelda: A Link to the Past")]
    [InlineData("Final Fantasy VII (USA) (Disc 1)", "Final Fantasy VII")]
    [InlineData("Pokemon - Edicion Roja (Spain) (SGB Enhanced)", "Pokemon: Edicion Roja")]
    [InlineData("Castlevania_-_Symphony_of_the_Night_(Europe)_(En,Fr,De)", "Castlevania: Symphony of the Night")]
    [InlineData("~Hack~ Super Mario World: Kaizo", "Super Mario World: Kaizo")]
    [InlineData("Metal Gear Solid [Subset - Bonus]", "Metal Gear Solid")]
    public void Limpia_nombres_de_rom(string raw, string expected) => Assert.Equal(expected, RomName.Clean(raw));

    [Theory]
    [InlineData(@"D:\Roms\SNES\Chrono Trigger (USA).sfc", "Chrono Trigger")]
    [InlineData(@"D:\Roms\GBA\Metroid Fusion (USA, Australia).zip#Metroid Fusion (USA, Australia).gba", "Metroid Fusion")]
    [InlineData("/home/u/roms/Sonic The Hedgehog (World) (Rev A).md", "Sonic The Hedgehog")]
    public void Limpia_nombres_de_archivo(string path, string expected) => Assert.Equal(expected, RomName.FromFile(path));

    [Fact]
    public void Nombre_limpio_empareja_con_sagas_y_la_lista()
    {
        var clean = RomName.Clean("Legend of Zelda, The - Ocarina of Time (USA) (Rev 2)");
        Assert.Equal("The Legend of Zelda", Sagas.Find(clean));
        Assert.True(TitleMatch.IsSameGame("The Legend of Zelda: Ocarina of Time", clean));
    }

    [Theory]
    [InlineData("SNES/Super Famicom", "SNES")]
    [InlineData("Nintendo - Super Nintendo Entertainment System", "SNES")]
    [InlineData("NES/Famicom", "NES")]
    [InlineData("Nintendo - Nintendo Entertainment System", "NES")]
    [InlineData("Genesis/Mega Drive", "Mega Drive")]
    [InlineData("Sega - Mega Drive - Genesis", "Mega Drive")]
    [InlineData("Sega - Mega-CD - Sega CD", "Sega CD")]
    [InlineData("Game Boy Advance", "GBA")]
    [InlineData("Nintendo - Game Boy Color", "GBC")]
    [InlineData("Game Boy", "GB")]
    [InlineData("Nintendo - Nintendo DS", "NDS")]
    [InlineData("Nintendo - Nintendo 3DS", "3DS")]
    [InlineData("Nintendo 64", "N64")]
    [InlineData("PlayStation", "PS1")]
    [InlineData("Sony - PlayStation 2", "PS2")]
    [InlineData("PlayStation Portable", "PSP")]
    [InlineData("PC Engine/TurboGrafx-16", "PC Engine")]
    [InlineData("FBNeo - Arcade Games", "Arcade")]
    [InlineData("snes9x_libretro", "SNES")]
    [InlineData("mgba_libretro", "GBA")]
    [InlineData("Sony - PlayStation (Beetle PSX HW)", "PS1")]
    [InlineData("Lista rara", null)]
    public void Reconoce_consolas(string text, string? expected) => Assert.Equal(expected, RetroPlatforms.Find(text));

    // ───────────────────────────── RetroArch ─────────────────────────────

    [Fact]
    public void Lee_lista_lpl_en_json()
    {
        var lpl = Write(@"RA\playlists\Nintendo - Super Nintendo Entertainment System.lpl", """
            {
              "version": "1.5",
              "default_core_path": "",
              "items": [
                { "path": "D:\\Roms\\SNES\\Super Mario World (USA).sfc", "label": "Super Mario World (USA)", "core_path": "DETECT", "core_name": "DETECT", "crc32": "B19ED489|crc", "db_name": "Nintendo - Super Nintendo Entertainment System.lpl" },
                { "path": "D:\\Roms\\SNES\\Chrono Trigger (USA).zip#Chrono Trigger (USA).sfc", "label": "", "core_path": "C:\\RetroArch\\cores\\snes9x_libretro.dll", "core_name": "Nintendo - SNES / SFC (Snes9x)", "crc32": "DETECT", "db_name": "" }
              ]
            }
            """);
        var games = RetroArchScanner.ReadPlaylist(lpl);
        Assert.Equal(new[] { "Super Mario World", "Chrono Trigger" }, games.Select(g => g.Title));
        Assert.All(games, g => { Assert.Equal("SNES", g.Platform); Assert.Equal("emulación", g.Note); Assert.True(g.Installed); });
        Assert.Equal("Chrono Trigger (USA)", games[1].ContentKey);
    }

    [Fact]
    public void Lee_lista_lpl_antigua_de_seis_lineas()
    {
        var lpl = Write(@"RA\playlists\old.lpl",
            "D:\\Roms\\GBA\\Golden Sun (USA, Europe).gba\nGolden Sun (USA, Europe)\nC:\\RetroArch\\cores\\mgba_libretro.dll\nNintendo - Game Boy Advance (mGBA)\n1D0D2B87|crc\nNintendo - Game Boy Advance.lpl\n" +
            "D:\\Roms\\MD\\Streets of Rage 2 (USA).md\nStreets of Rage 2 (USA)\nDETECT\nDETECT\nDETECT\nSega - Mega Drive - Genesis.lpl\n");
        var games = RetroArchScanner.ReadPlaylist(lpl);
        Assert.Equal(2, games.Count);
        Assert.Equal(("Golden Sun", "GBA"), (games[0].Title, games[0].Platform));
        Assert.Equal(("Streets of Rage 2", "Mega Drive"), (games[1].Title, games[1].Platform));
    }

    [Fact]
    public void RetroArch_con_tiempo_de_juego_y_sin_historiales_de_musica()
    {
        var root = Path.Combine(_tmp, "RetroArch");
        Write(@"RetroArch\retroarch.cfg", "playlist_directory = \":\\playlists\"\nruntime_log_directory = \"default\"\n");
        Write(@"RetroArch\playlists\Sony - PlayStation.lpl", """
            {"version":"1.5","items":[
              {"path":"E:\\PS1\\Final Fantasy VII (USA) (Disc 1).chd","label":"Final Fantasy VII (USA) (Disc 1)","core_path":"DETECT","core_name":"DETECT","db_name":"Sony - PlayStation.lpl"},
              {"path":"E:\\PS1\\Final Fantasy VII (USA) (Disc 2).chd","label":"Final Fantasy VII (USA) (Disc 2)","core_path":"DETECT","core_name":"DETECT","db_name":"Sony - PlayStation.lpl"}]}
            """);
        Write(@"RetroArch\playlists\content_music_history.lpl", """{"version":"1.5","items":[{"path":"C:\\musica\\cancion.mp3","label":"Canción","core_path":"builtin","core_name":"musicplayer"}]}""");
        Write(@"RetroArch\playlists\logs\Beetle PSX HW\Final Fantasy VII (USA) (Disc 1).lrtl", """{ "runtime": "10:30:00", "last_played": "2026-11-01 22:15:00" }""");
        Write(@"RetroArch\playlists\logs\Beetle PSX HW\Final Fantasy VII (USA) (Disc 2).lrtl", """{ "runtime": "2:00:00", "last_played": "2026-11-10 20:00:00" }""");

        var res = EmulatorScanner.Scan(new EmulatorPaths { RetroArch = new[] { root }, Dolphin = Array.Empty<string>(), Pcsx2 = Array.Empty<string>(), DuckStation = Array.Empty<string>() });
        var ff = Assert.Single(res.Games);                       // dos discos = un juego; la música no cuenta
        Assert.Equal(("Final Fantasy VII", "PS1"), (ff.Title, ff.Platform));
        Assert.Equal(10.5, ff.Hours);
        Assert.Equal(new DateTime(2026, 11, 10, 20, 0, 0), ff.LastPlayed);
        Assert.Contains("RetroArch: 1 juego", res.Message);
        Assert.Equal(12.5, RetroArchScanner.ParseRuntime("12:30:00"));
    }

    // ───────────────────────────── Dolphin, PCSX2 y DuckStation ─────────────────────────────

    private string Disc(string rel, int offset, uint magic, int size = 0x80)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var b = new byte[size];
        b[offset] = (byte)(magic >> 24); b[offset + 1] = (byte)(magic >> 16); b[offset + 2] = (byte)(magic >> 8); b[offset + 3] = (byte)magic;
        File.WriteAllBytes(p, b);
        return p;
    }

    [Fact]
    public void Dolphin_distingue_GameCube_y_Wii()
    {
        Assert.Equal("Wii", DolphinScanner.DetectPlatform(Disc(@"gc\Wii Sports (Europe).iso", 0x18, 0x5D1C9EA3)));
        Assert.Equal("GameCube", DolphinScanner.DetectPlatform(Disc(@"gc\Metroid Prime (USA).iso", 0x1C, 0xC2339F3D)));
        Assert.Equal("Wii", DolphinScanner.DetectPlatform(Disc(@"gc\Super Mario Galaxy (USA).rvz", 0x48, 2)));
        Assert.Equal("GameCube", DolphinScanner.DetectPlatform(Disc(@"gc\Pikmin (USA).rvz", 0x48, 1)));
        Assert.Equal("Wii", DolphinScanner.DetectPlatform(Path.Combine(_tmp, "x.wbfs")));
    }

    [Fact]
    public void Dolphin_lee_sus_carpetas_del_ini()
    {
        Disc(@"juegos\gc\Metroid Prime (USA).iso", 0x1C, 0xC2339F3D);
        Disc(@"juegos\gc\sub\Wii Sports (Europe) (En,Fr,De).iso", 0x18, 0x5D1C9EA3);
        Write(@"juegos\gc\leeme.txt", "no es un juego");
        var ini = Write(@"Dolphin\Config\Dolphin.ini", $"[General]\nISOPaths = 1\nISOPath0 = {Path.Combine(_tmp, "juegos", "gc")}\nRecursiveISOPaths = True\n[Core]\nCPUThread = True\n");
        var games = DolphinScanner.Scan(new[] { ini })!.OrderBy(g => g.Title).ToList();
        Assert.Equal(new[] { ("Metroid Prime", "GameCube"), ("Wii Sports", "Wii") }, games.Select(g => (g.Title, g.Platform!)));
        Assert.Null(DolphinScanner.Scan(new[] { Path.Combine(_tmp, "no-existe.ini") }));   // no instalado
    }

    [Fact]
    public void DuckStation_sin_pistas_sueltas_ni_discos_repetidos()
    {
        var dir = Path.Combine(_tmp, "ps1");
        Write(@"ps1\Crash Bandicoot (USA).cue", "FILE \"Crash Bandicoot (USA).bin\" BINARY");
        Write(@"ps1\Crash Bandicoot (USA).bin", "x");
        Write(@"ps1\Metal Gear Solid (USA) (Disc 1).chd", "x");
        Write(@"ps1\Metal Gear Solid (USA) (Disc 2).chd", "x");
        Write(@"ps1\Metal Gear Solid (USA).m3u", "Metal Gear Solid (USA) (Disc 1).chd\nMetal Gear Solid (USA) (Disc 2).chd\n");
        Write(@"ps1\sueltos\Spyro the Dragon (Europe).pbp", "x");
        var ini = Write(@"DuckStation\settings.ini", $"[Main]\nSettingsVersion = 3\n\n[GameList]\nRecursivePaths = {dir}\n");

        var res = EmulatorScanner.Scan(new EmulatorPaths { RetroArch = Array.Empty<string>(), Dolphin = Array.Empty<string>(), Pcsx2 = Array.Empty<string>(), DuckStation = new[] { ini } });
        Assert.Equal(new[] { "Crash Bandicoot", "Metal Gear Solid", "Spyro the Dragon" }, res.Games.Select(g => g.Title).OrderBy(t => t));
        Assert.All(res.Games, g => Assert.Equal("PS1", g.Platform));
        Assert.Equal("Emuladores: DuckStation: 3 juegos.", res.Message);
    }

    [Fact]
    public void PCSX2_solo_lee_su_seccion_y_no_recursivo()
    {
        Write(@"ps2\Shadow of the Colossus (USA).iso", "x");
        Write(@"ps2\otra\Okami (USA).chd", "x");
        var ini = Write(@"PCSX2\inis\PCSX2.ini", $"[UI]\nPaths = C:\\no\\leer\n[GameList]\nPaths = {Path.Combine(_tmp, "ps2")}\n");
        var games = DiscFolderScanner.Scan(new[] { ini }, "PS2", DiscFolderScanner.Pcsx2Extensions)!;
        var g = Assert.Single(games);
        Assert.Equal(("Shadow of the Colossus", "PS2"), (g.Title, g.Platform));
    }

    [Fact]
    public void Emuladores_no_encontrados()
    {
        var none = Array.Empty<string>();
        var res = EmulatorScanner.Scan(new EmulatorPaths { RetroArch = none, Dolphin = none, Pcsx2 = none, DuckStation = none });
        Assert.Empty(res.Games);
        Assert.Contains("No se ha encontrado", res.Message);
    }

    [Fact]
    public void Plan_marca_los_que_ya_estan_en_la_lista()
    {
        var doc = DocumentService.Parse("Lista\r\n\r\nFinal Fantasy:\r\n\r\n-Final Fantasy VII [emulación] - PS1 #01/02/2020#\r\n\r\n<Por jugar>\r\n\r\n", FileKind.Txt);
        var games = new List<ImportCandidate>
        {
            EmulatorScanner.Candidate(RomName.Clean("Final Fantasy VII (USA) (Disc 1)"), "PS1"),
            EmulatorScanner.Candidate(RomName.Clean("Final Fantasy IX (USA) (Disc 1)"), "PS1")
        };
        ImportPlanner.Plan(doc, games, new DateTime(2026, 11, 20));
        Assert.Equal("Completados", games[0].DuplicateOf);
        Assert.False(games[0].Selected);
        Assert.Null(games[1].DuplicateOf);
        Assert.Equal("Final Fantasy", games[1].SuggestedCategory);
        Assert.Equal("Por jugar", games[1].SuggestedSection);
    }

    [Fact]
    public void Mismo_juego_en_dos_consolas_no_se_une()
    {
        var merged = ImportPlanner.Merge(new[]
        {
            EmulatorScanner.Candidate("Resident Evil", "PS1"),
            EmulatorScanner.Candidate("Resident Evil", "GameCube"),
            EmulatorScanner.Candidate("Resident Evil", "PS1")
        });
        Assert.Equal(2, merged.Count);
    }
}
