using System.Text;
using System.Text.Json;
using ListaJuegos.Core;
using ListaJuegos.Core.Cli;

namespace ListaJuegos.Tests;

/// <summary>Fase 4: argumentos de la línea de comandos y órdenes --add, --complete y --stats sobre archivos temporales.</summary>
public class CliParserTests
{
    private static CliOptions P(params string[] a) => CliParser.Parse(a);

    [Fact]
    public void Sin_argumentos_abre_la_ventana()
    {
        var o = P();
        Assert.False(o.IsCli);
        Assert.Equal(CliCommand.None, o.Command);
    }

    [Theory]
    [InlineData(@"C:\Listas\Juegos que me he pasado.txt")]
    [InlineData(@"D:\lista.md")]
    [InlineData("lista-sin-guiones.txt")]
    public void Solo_una_ruta_abre_la_ventana_como_antes(string path)
    {
        Assert.False(P(path).IsCli);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    [InlineData("--HELP")]
    public void Ayuda(string arg)
    {
        var o = P(arg);
        Assert.True(o.IsCli);
        Assert.Equal(CliCommand.Help, o.Command);
        Assert.Null(o.Error);
    }

    [Fact]
    public void Add_con_todas_las_opciones()
    {
        var o = P("--add", "Hollow Knight: Silksong", "--platform", "Switch 2", "--section", "Jugando ahora", "--saga", "Hollow Knight", "--file", @"C:\x\lista.txt");
        Assert.Null(o.Error);
        Assert.Equal(CliCommand.Add, o.Command);
        Assert.Equal("Hollow Knight: Silksong", o.Title);
        Assert.Equal("Switch 2", o.Platform);
        Assert.Equal("Jugando ahora", o.Section);
        Assert.Equal("Hollow Knight", o.Saga);
        Assert.Equal(@"C:\x\lista.txt", o.File);
    }

    [Fact]
    public void Admite_opcion_igual_valor_y_ruta_suelta()
    {
        var o = P(@"C:\x\lista.md", "--complete=Celeste");
        Assert.Null(o.Error);
        Assert.Equal(CliCommand.Complete, o.Command);
        Assert.Equal("Celeste", o.Title);
        Assert.Equal(@"C:\x\lista.md", o.File);
    }

    [Fact]
    public void Stats_json()
    {
        var o = P("--stats", "--json");
        Assert.Equal(CliCommand.Stats, o.Command);
        Assert.True(o.Json);
        Assert.Null(o.File);
    }

    [Theory]
    [InlineData("Falta el valor de --add", "--add")]
    [InlineData("Falta el valor de --platform", "--add", "Celeste", "--platform")]
    [InlineData("--add necesita --platform", "--add", "Celeste")]
    [InlineData("Opción desconocida: --borrar", "--borrar", "Celeste")]
    [InlineData("Solo se puede dar una orden", "--add", "Celeste", "--platform", "PC", "--stats")]
    [InlineData("--json solo se usa con --stats", "--complete", "Celeste", "--json")]
    [InlineData("--saga solo se usa con --add", "--complete", "Celeste", "--saga", "X")]
    [InlineData("Falta la orden", "--file", "lista.txt")]
    [InlineData("aparece dos veces", "--stats", "--file", "a.txt", "--file", "b.txt")]
    [InlineData("Argumento de más", "--stats", "a.txt", "b.txt")]
    [InlineData("El título está vacío", "--complete", "")]
    [InlineData("no lleva valor", "--stats=sí")]
    public void Errores_de_uso(string expected, params string[] args)
    {
        var o = P(args);
        Assert.True(o.IsCli);
        Assert.NotNull(o.Error);
        Assert.Contains(expected, o.Error);
    }

    [Fact]
    public void Error_de_uso_devuelve_2_y_lo_explica()
    {
        var (code, outText, err) = CliHarness.Run(CliParser.Parse(new[] { "--add", "X" }), new CliContext());
        Assert.Equal(CliExit.Usage, code);
        Assert.Equal("", outText);
        Assert.Contains("--platform", err);
        Assert.Contains("--help", err);
    }

    [Fact]
    public void La_ayuda_lista_las_ordenes()
    {
        var (code, outText, _) = CliHarness.Run(CliParser.Parse(new[] { "--help" }), new CliContext());
        Assert.Equal(0, code);
        foreach (var s in new[] { "--add", "--complete", "--stats", "--json", "--file", "--help" }) Assert.Contains(s, outText);
    }
}

internal static class CliHarness
{
    public static (int code, string output, string error) Run(CliOptions o, CliContext ctx)
    {
        var w = new StringWriter(); var e = new StringWriter();
        var code = CliRunner.Run(o, ctx, w, e);
        return (code, w.ToString(), e.ToString());
    }
}

public sealed class CliRunnerTests : IDisposable
{
    private const string Lista =
        "Lista de los juegos que me he pasado (Actualizado el 01/11/2026)\r\n\r\n" +
        "Resident Evil:\r\n\r\n" +
        "-Resident Evil 4 - PS2 #10/07/2006# {★ 9/10} {20 h}\r\n" +
        "-Resident Evil 7 - PS4 #01/02/2024# {★ 8/10} {12 h}\r\n\r\n" +
        "(Indies)\r\n\r\n" +
        "-Celeste - Switch #14/02/2026# {★ 9/10} {12 h}\r\n\r\n" +
        "<Inconclusos>\r\n\r\n" +
        "Persona:\r\n\r\n" +
        "-Persona 4 Golden - PC {20 h}\r\n\r\n" +
        "<Por jugar>\r\n\r\n" +
        "-Hades - PC\r\n\r\n" +
        "Resident Evil:\r\n\r\n" +
        "-Resident Evil 4 (Remake) - PC {ttb: 16h}\r\n" +
        "-Resident Evil Village - PS5\r\n\r\n" +
        "(Indies)\r\n\r\n" +
        "-Hollow Knight - Switch\r\n" +
        "-Hollow Knight: Silksong - Switch\r\n";

    private static readonly DateTime Hoy = new(2026, 11, 21);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lj-cli-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;
    private readonly byte[] _original;

    public CliRunnerTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "Juegos que me he pasado.txt");
        _original = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Lista)).ToArray();
        File.WriteAllBytes(_file, _original);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private (int code, string output, string error) Run(params string[] args) =>
        CliHarness.Run(CliParser.Parse(args.Append("--file").Append(_file).ToArray()), new CliContext { Today = Hoy, Backup = true, UpdateDate = true });

    private string Text() => Encoding.UTF8.GetString(File.ReadAllBytes(_file).AsSpan(3));
    private string[] Lines() => Text().Split("\r\n");

    /// <summary>Las líneas que no deben cambiar siguen idénticas y en el mismo orden.</summary>
    private void AssertOnlyChanged(params string[] allowedNew)
    {
        var before = Lista.Split("\r\n").Skip(1).ToList();
        var after = Lines().Skip(1).ToList();
        var removedFromBefore = before.Where(l => !after.Contains(l)).ToList();
        var added = after.Where(l => !before.Contains(l)).ToList();
        Assert.All(added, l => Assert.Contains(l, allowedNew));
        Assert.All(removedFromBefore, l => Assert.StartsWith("-", l));
    }

    [Fact]
    public void Add_por_defecto_va_a_por_jugar_suelto_con_bak()
    {
        var (code, output, error) = Run("--add", "Tunic", "--platform", "PC/Xbox Series");
        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Contains("Añadido «Tunic» (PC / Xbox Series) a «Por jugar»", output);
        var lines = Lines();
        Assert.Equal("Lista de los juegos que me he pasado (Actualizado el 21/11/2026)", lines[0]);
        var i = Array.IndexOf(lines, "-Hades - PC");
        Assert.Equal("-Tunic - PC / Xbox Series", lines[i + 1]);
        AssertOnlyChanged("Lista de los juegos que me he pasado (Actualizado el 21/11/2026)", "-Tunic - PC / Xbox Series");
        // Copia .bak con el archivo de antes, byte a byte (BOM incluido)
        Assert.Equal(_original, File.ReadAllBytes(_file + ".bak"));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(_file)[..3]);
    }

    [Fact]
    public void Add_en_saga_existente_y_seccion_por_nombre_o_tipo()
    {
        Assert.Equal(0, Run("--add", "Resident Evil Requiem", "--platform", "PS5", "--section", "por JUGAR", "--saga", "resident evil").code);
        var lines = Lines();
        var village = Array.IndexOf(lines, "-Resident Evil Village - PS5");
        Assert.Equal("-Resident Evil Requiem - PS5", lines[village + 1]);

        // «pendientes» no es el nombre, pero es el mismo tipo de sección
        Assert.Equal(0, Run("--add", "Tunic", "--platform", "PC", "--section", "pendientes").code);
        Assert.Contains("-Tunic - PC", Lines());
    }

    [Fact]
    public void Add_en_completados_pone_la_fecha_de_hoy_y_crea_la_saga()
    {
        var (code, output, _) = Run("--add", "Halo Infinite", "--platform", "Xbox Series", "--section", "Completados", "--saga", "Halo");
        Assert.Equal(0, code);
        Assert.Contains("saga «Halo»", output);
        var t = Text();
        Assert.Contains("Halo:\r\n\r\n-Halo Infinite - Xbox Series #21/11/2026#\r\n", t);
        // La saga nueva va al final de Completados, antes de <Inconclusos>
        Assert.True(t.IndexOf("Halo:", StringComparison.Ordinal) < t.IndexOf("<Inconclusos>", StringComparison.Ordinal));
        Assert.True(t.IndexOf("(Indies)", StringComparison.Ordinal) < t.IndexOf("Halo:", StringComparison.Ordinal));
        // Y se vuelve a leer igual
        var doc = DocumentService.Load(_file);
        Assert.Contains(doc.Sections[0].Blocks, b => b.Name == "Halo" && b.Entries.Single().Date == "21/11/2026");
    }

    [Fact]
    public void Add_rechaza_repetidos_y_secciones_que_no_existen()
    {
        var (code, _, error) = Run("--add", "  celeste ", "--platform", "PC");
        Assert.Equal(CliExit.NotFound, code);
        Assert.Contains("«Celeste» ya está en la lista", error);

        (code, _, error) = Run("--add", "Tunic", "--platform", "PC", "--section", "Pendientísimos XL");
        Assert.Equal(CliExit.NotFound, code);
        Assert.Contains("No hay ninguna sección «Pendientísimos XL»", error);
        Assert.Contains("«Por jugar»", error);

        // Nada se ha escrito
        Assert.Equal(_original, File.ReadAllBytes(_file));
        Assert.False(File.Exists(_file + ".bak"));
    }

    [Fact]
    public void Add_admite_un_titulo_parecido_pero_distinto()
    {
        // «Resident Evil 7 (Gold Edition)» no es «Resident Evil 7»: se añade
        Assert.Equal(0, Run("--add", "Resident Evil 2", "--platform", "PC").code);
        Assert.Contains("-Resident Evil 2 - PC", Lines());
    }

    [Fact]
    public void Complete_mueve_a_completados_misma_saga_con_fecha()
    {
        var (code, output, error) = Run("--complete", "resident evil village");
        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Contains("Completado «Resident Evil Village»: movido a «Completados», saga «Resident Evil» con fecha 21/11/2026", output);
        var lines = Lines();
        var re7 = Array.IndexOf(lines, "-Resident Evil 7 - PS4 #01/02/2024# {★ 8/10} {12 h}");
        Assert.Equal("-Resident Evil Village - PS5 #21/11/2026#", lines[re7 + 1]);
        Assert.Single(lines, l => l.StartsWith("-Resident Evil Village", StringComparison.Ordinal));
        AssertOnlyChanged("Lista de los juegos que me he pasado (Actualizado el 21/11/2026)", "-Resident Evil Village - PS5 #21/11/2026#");
        Assert.True(File.Exists(_file + ".bak"));
    }

    [Fact]
    public void Complete_de_un_genero_va_al_mismo_genero()
    {
        Assert.Equal(0, Run("--complete", "Hades").code);   // suelto → suelto en Completados
        Assert.Equal(0, Run("--complete", "Silksong").code);  // parte del título, solo coincide uno
        var t = Text();
        var completados = t[..t.IndexOf("<Inconclusos>", StringComparison.Ordinal)];
        Assert.Contains("(Indies)\r\n\r\n-Celeste - Switch #14/02/2026# {★ 9/10} {12 h}\r\n-Hollow Knight: Silksong - Switch #21/11/2026#", completados);
        Assert.Contains("-Hades - PC #21/11/2026#", completados);
    }

    [Fact]
    public void Complete_conserva_los_campos_y_prefiere_el_titulo_exacto()
    {
        // «Resident Evil 4 (Remake)» está pendiente; «Resident Evil 4» ya completado: con el título exacto no hay duda
        Assert.Equal(0, Run("--complete", "Resident Evil 4 (Remake)").code);
        Assert.Contains("-Resident Evil 4 (Remake) - PC #21/11/2026# {ttb: 16h}", Lines());
    }

    [Fact]
    public void Complete_ambiguo_no_encontrado_o_ya_completado()
    {
        var (code, _, error) = Run("--complete", "Hollow");
        Assert.Equal(CliExit.NotFound, code);
        Assert.Contains("coincide con 2 juegos", error);
        Assert.Contains("«Hollow Knight: Silksong» (Por jugar)", error);

        (code, _, error) = Run("--complete", "Zelda");
        Assert.Equal(CliExit.NotFound, code);
        Assert.Contains("No hay ningún juego que se llame «Zelda»", error);

        var (c2, output, _) = Run("--complete", "Celeste");
        Assert.Equal(0, c2);
        Assert.Contains("ya estaba completado", output);

        Assert.Equal(_original, File.ReadAllBytes(_file));
    }

    [Fact]
    public void Stats_json_estructurado()
    {
        var (code, output, error) = Run("--stats", "--json");
        Assert.Equal(0, code);
        Assert.Equal("", error);
        using var j = JsonDocument.Parse(output);
        var r = j.RootElement;
        Assert.Equal(_file, r.GetProperty("file").GetString());
        Assert.Equal(9, r.GetProperty("total").GetInt32());
        Assert.Equal(3, r.GetProperty("completed").GetInt32());
        Assert.Equal(64, r.GetProperty("hours").GetDouble());
        Assert.Equal(8.67, r.GetProperty("rating").GetDouble());
        var secs = r.GetProperty("sections").EnumerateArray().Select(s => (s.GetProperty("name").GetString()!, s.GetProperty("kind").GetString()!, s.GetProperty("games").GetInt32(), s.GetProperty("hours").GetDouble())).ToList();
        Assert.Equal(new[] { ("Completados", "completed", 3, 44.0), ("Inconclusos", "unfinished", 1, 20.0), ("Por jugar", "backlog", 5, 0.0) }, secs.ToArray());
        // Solo lee: el archivo no cambia
        Assert.Equal(_original, File.ReadAllBytes(_file));
    }

    [Fact]
    public void Stats_en_texto()
    {
        var (code, output, _) = Run("--stats");
        Assert.Equal(0, code);
        Assert.Contains("Juegos que me he pasado.txt", output);
        Assert.Matches(@"Completados\s+3 juegos\s+44 h", output);
        Assert.Matches(@"Por jugar\s+5 juegos\s+—", output);
        Assert.Matches(@"Total\s+9 juegos\s+64 h", output);
        Assert.Contains("Nota media: 8,7/10 (3 juegos con nota)", output);
    }

    [Fact]
    public void Funciona_con_md()
    {
        var md = Path.Combine(_dir, "lista.md");
        DocumentService.Save(DocumentService.Load(_file), md, backup: false);
        var o = CliParser.Parse(new[] { "--complete", "Hades", "--file", md });
        var (code, _, _) = CliHarness.Run(o, new CliContext { Today = Hoy, Backup = false });
        Assert.Equal(0, code);
        var doc = DocumentService.Load(md);
        Assert.Contains(doc.Sections[0].Entries, e => e.Title == "Hades" && e.Date == "21/11/2026");
        Assert.False(File.Exists(md + ".bak"));   // copia desactivada en Ajustes
    }

    [Fact]
    public void Usa_la_ultima_lista_abierta_y_avisa_si_no_hay()
    {
        var o = CliParser.Parse(new[] { "--stats", "--json" });
        var (code, output, _) = CliHarness.Run(o, new CliContext { DefaultFile = _file });
        Assert.Equal(0, code);
        Assert.Contains("\"total\": 9", output);

        var (c2, _, err) = CliHarness.Run(o, new CliContext());
        Assert.Equal(CliExit.Usage, c2);
        Assert.Contains("--file", err);

        var (c3, _, err3) = CliHarness.Run(CliParser.Parse(new[] { "--stats", "--file", Path.Combine(_dir, "no-existe.txt") }), new CliContext());
        Assert.Equal(CliExit.Failure, c3);
        Assert.Contains("no existe", err3);
    }

    [Fact]
    public void Sin_fecha_de_actualizacion_si_esta_desactivada()
    {
        var o = CliParser.Parse(new[] { "--add", "Tunic", "--platform", "PC", "--file", _file });
        CliHarness.Run(o, new CliContext { Today = Hoy, UpdateDate = false, Backup = false });
        Assert.Equal("Lista de los juegos que me he pasado (Actualizado el 01/11/2026)", Lines()[0]);
        Assert.False(File.Exists(_file + ".bak"));
    }
}
