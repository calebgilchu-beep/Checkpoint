using System.Net;
using System.Text;
using System.Text.Json;
using ListaJuegos.Core;
using ListaJuegos.Core.Connectors;
using ListaJuegos.Core.Parsing;

namespace ListaJuegos.Tests;

/// <summary>Duración desde IGDB (API oficial de Twitch), sin red: un servidor simulado responde como IGDB.</summary>
public class IgdbTests
{
    static IgdbTests() => IgdbClient.MinInterval = TimeSpan.Zero;

    private const string Games = """
        [{"id":14593,"name":"Hollow Knight","slug":"hollow-knight","first_release_date":1487894400,"platforms":[{"id":6,"abbreviation":"PC"},{"id":130,"abbreviation":"Switch"}]},
         {"id":119388,"name":"Hollow Knight: Silksong","slug":"hollow-knight-silksong","first_release_date":1756944000,"platforms":[{"id":6,"abbreviation":"PC"}]},
         {"id":999,"name":"Hollow Knight: Voidheart Edition","slug":"hk-voidheart","platforms":[{"id":48,"abbreviation":"PS4"}]},
         {"id":5,"name":"Sin tiempos"}]
        """;
    private const string Times = """
        [{"id":1,"game_id":14593,"hastily":72000,"normally":97200,"completely":226800,"count":124},
         {"id":2,"game_id":119388,"hastily":90000,"normally":108000,"count":40},
         {"id":3,"game_id":999,"normally":99000,"count":3}]
        """;

    /// <summary>Servidor simulado: token de Twitch y endpoints de IGDB; guarda las peticiones.</summary>
    private sealed class FakeIgdb : HttpMessageHandler
    {
        public readonly List<(string url, string body, string? clientId, string? auth)> Calls = new();
        public Func<HttpRequestMessage, HttpResponseMessage?>? Override;
        public int Tokens;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct);
            Calls.Add((r.RequestUri!.ToString(), body,
                r.Headers.TryGetValues("Client-ID", out var c) ? c.First() : null,
                r.Headers.TryGetValues("Authorization", out var a) ? a.First() : null));
            if (Override?.Invoke(r) is { } o) return o;
            var url = r.RequestUri!.ToString();
            if (url.StartsWith(IgdbClient.TokenUrl)) { Tokens++; return Json($$"""{"access_token":"tok{{Tokens}}","expires_in":5000000,"token_type":"bearer"}"""); }
            if (url.EndsWith("/games")) return Json(body.Contains("Hollow Knight") ? Games : "[]");
            if (url.EndsWith("/game_time_to_beats")) return Json(Times);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
        public static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };
    }

    private static string NewId() => "cid" + Guid.NewGuid().ToString("N")[..10];   // cada prueba con su token

    [Fact]
    public async Task Busca_con_token_de_Twitch_y_devuelve_la_duracion()
    {
        var fake = new FakeIgdb();
        var id = NewId();
        var r = await new IgdbClient(new HttpClient(fake), id, "secreto").FindAsync("Hollow Knight", "PC / Steam Deck");

        var best = r[0];
        Assert.Equal("Hollow Knight", best.Name);
        Assert.True(best.Exact);
        Assert.Equal(27, best.Main);      // 97 200 s
        Assert.Equal(63, best.Full);      // 226 800 s
        Assert.Equal(124, best.Count);
        Assert.Equal(2017, best.Year);
        Assert.Equal("ttb: 27h / 63h", best.Field);
        Assert.Equal("https://www.igdb.com/games/hollow-knight", best.Url);
        Assert.DoesNotContain(r, x => x.Name == "Sin tiempos");

        // Token: POST a Twitch con client_credentials; luego cabeceras Client-ID y Bearer en IGDB
        Assert.StartsWith($"{IgdbClient.TokenUrl}?client_id={id}&client_secret=secreto&grant_type=client_credentials", fake.Calls[0].url);
        var games = fake.Calls[1];
        Assert.Equal("https://api.igdb.com/v4/games", games.url);
        Assert.Contains("search \"Hollow Knight\";", games.body);
        Assert.Equal(id, games.clientId);
        Assert.Equal("Bearer tok1", games.auth);
        var times = fake.Calls[2];
        Assert.Equal("https://api.igdb.com/v4/game_time_to_beats", times.url);
        Assert.Contains("where game_id = (14593,119388,999,5);", times.body);
    }

    [Fact]
    public async Task Reutiliza_el_token_y_lo_renueva_si_caduca()
    {
        var fake = new FakeIgdb();
        var c = new IgdbClient(new HttpClient(fake), NewId(), "s");
        await c.FindAsync("Hollow Knight");
        await c.FindAsync("Hollow Knight");
        Assert.Equal(1, fake.Tokens);

        var first = true;
        fake.Override = r => r.RequestUri!.ToString().EndsWith("/games") && first ? (first = false) == false ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : null : null;
        var res = await c.FindAsync("Hollow Knight");
        Assert.Equal(2, fake.Tokens);       // 401 → token nuevo y se repite una vez
        Assert.NotEmpty(res);
    }

    [Fact]
    public async Task Credenciales_rechazadas_dan_un_mensaje_claro()
    {
        var fake = new FakeIgdb { Override = r => r.RequestUri!.ToString().StartsWith(IgdbClient.TokenUrl) ? new HttpResponseMessage(HttpStatusCode.BadRequest) : null };
        var e = await Assert.ThrowsAsync<ConnectorException>(() => new IgdbClient(new HttpClient(fake), NewId(), "malo").FindAsync("Hollow Knight"));
        Assert.Contains("Client ID o el secreto", e.Message);
    }

    [Fact]
    public async Task Las_comillas_del_titulo_no_rompen_la_consulta()
    {
        var fake = new FakeIgdb();
        await new IgdbClient(new HttpClient(fake), NewId(), "s").FindAsync("Hollow \"Knight\" (Remake) [GOTY]");
        Assert.Contains("search \"Hollow  Knight\";", fake.Calls.Single(c => c.url.EndsWith("/games")).body);
    }

    [Fact]
    public async Task Sin_resultados_no_pide_tiempos()
    {
        var fake = new FakeIgdb();
        var r = await new IgdbClient(new HttpClient(fake), NewId(), "s").FindAsync("Juego que no existe");
        Assert.Empty(r);
        Assert.DoesNotContain(fake.Calls, c => c.url.EndsWith("/game_time_to_beats"));
    }

    [Theory]
    [InlineData(97200, 27)]
    [InlineData(5400, 1.5)]
    [InlineData(6300, 2)]        // 1,75 h → 2 h (a la media hora más cercana)
    [InlineData(600, 0.5)]       // 10 min → mínimo media hora
    public void Segundos_a_horas(int s, double h) => Assert.Equal(h, IgdbClient.Hours(s));

    [Fact]
    public void Prefiere_el_titulo_exacto_y_la_plataforma()
    {
        var games = IgdbClient.ParseGames(JsonDocument.Parse(Games).RootElement);
        var times = IgdbClient.ParseTimes(JsonDocument.Parse(Times).RootElement);
        Assert.Equal("Hollow Knight", IgdbClient.Rank("hollow knight", null, games, times)[0].Name);
        var silk = IgdbClient.Rank("Hollow Knight Silksong", "PC", games, times);
        Assert.Equal("Hollow Knight: Silksong", silk[0].Name);
        Assert.Null(silk[0].Full);
        Assert.Equal("ttb: 30h", silk[0].Field);
        // «Voidheart Edition» cuenta como el mismo juego («edition» es una palabra de más)
        Assert.Contains(IgdbClient.Rank("Hollow Knight: Voidheart Edition", "PS4", games, times), x => x.Exact && x.Name.Contains("Voidheart"));
        // Un título que no coincide se devuelve, pero sin marcarlo como seguro
        Assert.All(IgdbClient.Rank("Celeste", null, games, times), x => Assert.False(x.Exact));
    }

    [Fact]
    public void Enlace_de_HowLongToBeat_sin_etiquetas()
    {
        Assert.Equal("https://howlongtobeat.com/?q=Resident%20Evil%204", IgdbClient.HltbSearchUrl("Resident Evil 4 (Remake)"));
    }

    [Fact]
    public async Task Rellenar_solo_usa_coincidencias_claras_y_respeta_lo_que_hay()
    {
        const string lista = "Lista\r\n\r\n<Por jugar>\r\n\r\n-Hollow Knight - PC\r\n-Hollow Knight Silksong - PC\r\n-Celeste - Switch\r\n-Hades - PC {ttb: 22h}\r\n-Juego raro - PC\r\n";
        var doc = TxtFormat.Parse(lista);
        var fake = new FakeIgdb();
        var c = new IgdbClient(new HttpClient(fake), NewId(), "s");
        var asked = new List<string>();
        var games = IgdbClient.ParseGames(JsonDocument.Parse(Games).RootElement);
        var times = IgdbClient.ParseTimes(JsonDocument.Parse(Times).RootElement);
        var report = await DurationFiller.FillAsync(doc.Sections[1], (t, p, ct) =>
        {
            asked.Add(t);
            // Para «Celeste» IGDB solo devuelve juegos de otro nombre
            return t.StartsWith("Hollow") ? c.FindAsync(t, p, ct)
                 : t == "Celeste" ? Task.FromResult(IgdbClient.Rank(t, p, games, times))
                 : Task.FromResult(new List<IgdbDuration>());
        });

        Assert.DoesNotContain("Hades", asked);                                   // ya tenía duración
        Assert.Equal(1, report.Skipped);
        Assert.Equal(new[] { "Hollow Knight", "Hollow Knight Silksong" }, report.Changes.Select(x => x.Title));
        Assert.Equal("ttb: 27h / 63h", report.Changes[0].Field);
        Assert.Equal("ttb: 30h", report.Changes[1].Field);
        Assert.Equal(new[] { "Celeste" }, report.Unsure);                        // IGDB devolvió otro título: no se escribe
        Assert.Equal(new[] { "Juego raro" }, report.NotFound);
        Assert.Equal(lista, TxtFormat.Write(doc));                               // el relleno no toca el documento
    }

    [Fact]
    public void El_campo_se_escribe_en_su_sitio_y_el_resto_queda_igual()
    {
        const string lista = "Lista\r\n\r\n<Por jugar>\r\n\r\n-Hollow Knight - PC {steam:367520}\r\n-Celeste - Switch\r\n";
        var doc = TxtFormat.Parse(lista);
        var e = doc.Sections[1].Entries.First();
        Fields.Set(e, FieldKind.Duration, "ttb: 27h / 63h");
        Assert.Equal("Lista\r\n\r\n<Por jugar>\r\n\r\n-Hollow Knight - PC {steam:367520} {ttb: 27h / 63h}\r\n-Celeste - Switch\r\n", TxtFormat.Write(doc));
    }
}
