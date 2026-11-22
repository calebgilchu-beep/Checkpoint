using ListaJuegos.Core;
using ListaJuegos.Core.Parsing;
using ListaJuegos.Core.Suggest;

namespace ListaJuegos.Tests;

/// <summary>Fase 2: campo {ttb: …} (duración estimada) y «¿Qué juego ahora?».</summary>
public class DurationTests
{
    private const string Lista =
        "Lista de los juegos que me he pasado (Actualizado el 01/11/2026)\r\n\r\n" +
        "Resident Evil:\r\n\r\n" +
        "-Resident Evil 4 (Remake) - PC #10/07/2026#\r\n" +
        "-Resident Evil 7 - PS4 #01/02/2025#\r\n\r\n" +
        "Final Fantasy:\r\n\r\n" +
        "-Final Fantasy X - PS2 #05/05/2010#\r\n\r\n" +
        "<Inconclusos>\r\n\r\n" +
        "Persona:\r\n\r\n" +
        "-Persona 4 Golden - PC {20 h}\r\n\r\n" +
        "<Por jugar>\r\n\r\n" +
        "Resident Evil:\r\n\r\n" +
        "-Resident Evil Village - PC {ttb: 9h / 25h}\r\n\r\n" +
        "Persona:\r\n\r\n" +
        "-Persona 5 Royal - Switch {ttb: 100h}\r\n\r\n" +
        "Metroid:\r\n\r\n" +
        "-Metroid Dread - Switch {ttb: 8h}\r\n\r\n" +
        "(Indies)\r\n\r\n" +
        "-Portal 2 - PC {ttb: 6h}\r\n" +
        "-Final Fantasy XII - PS2\r\n" +
        "-Dark Souls III - PC\r\n";

    private static GameDocument Doc() => DocumentService.Parse(Lista, FileKind.Txt);

    // ───────────────────────────── campo {ttb: …} ─────────────────────────────

    [Theory]
    [InlineData("ttb: 12h", 12.0, null)]
    [InlineData("ttb: 12h / 35h", 12.0, 35.0)]
    [InlineData("ttb:12h/35h", 12.0, 35.0)]
    [InlineData("TTB: 12,5 h", 12.5, null)]
    [InlineData("ttb: 40", 40.0, null)]
    public void Lee_la_duracion(string field, double main, double? full)
    {
        Assert.Equal(FieldKind.Duration, Fields.Classify(field));
        Assert.Equal((main, full), Fields.ParseDuration(field));
    }

    [Theory]
    [InlineData("ttb: mucho")]
    [InlineData("ttb: 12h / ")]
    [InlineData("tiempo: 12h")]
    public void No_confunde_otros_campos(string field) => Assert.NotEqual(FieldKind.Duration, Fields.Classify(field));

    [Fact]
    public void Escribe_la_duracion()
    {
        Assert.Equal("ttb: 12h", Fields.MakeDuration(12));
        Assert.Equal("ttb: 12h / 35h", Fields.MakeDuration(12, 35));
        Assert.Equal("ttb: 12,5h / 40h", Fields.MakeDuration(12.5, 40));
        Assert.Equal("ttb: 12h", Fields.MakeDuration(12, 0));
    }

    [Fact]
    public void Txt_ida_y_vuelta_identico()
    {
        // Incluye escrituras a mano distintas de las que genera la app: se respetan tal cual.
        const string txt = "Lista\r\n\r\n-Hades - PC {ttb: 22h / 95h} {steam:1145360}\r\n-Celeste - Switch  {ttb:8 h}\r\n-Tetris - GB\r\n";
        var doc = DocumentService.Parse(txt, FileKind.Txt);
        Assert.Equal(txt, DocumentService.Serialize(doc, FileKind.Txt));
        var es = doc.Sections.SelectMany(s => s.Entries).ToList();
        Assert.Equal((22.0, (double?)95.0), Fields.Duration(es[0]));
        Assert.Equal((8.0, (double?)null), Fields.Duration(es[1]));
        Assert.Null(Fields.Duration(es[2]));
    }

    [Fact]
    public void Anadir_duracion_solo_cambia_esa_linea()
    {
        var doc = Doc();
        var dark = doc.Sections.SelectMany(s => s.Entries).Single(e => e.Title == "Dark Souls III");
        Fields.Set(dark, FieldKind.Duration, Fields.MakeDuration(32, 84));
        var after = DocumentService.Serialize(doc, FileKind.Txt);

        var a = Lista.Split("\r\n"); var b = after.Split("\r\n");
        Assert.Equal(a.Length, b.Length);
        var diff = a.Zip(b).Where(p => p.First != p.Second).ToList();
        var d = Assert.Single(diff);
        Assert.Equal("-Dark Souls III - PC", d.First);
        Assert.Equal("-Dark Souls III - PC {ttb: 32h / 84h}", d.Second);
    }

    [Fact]
    public void Cambiar_duracion_sustituye_el_campo_en_su_sitio()
    {
        var e = EntryLine.Parse("Hades - PC {42 h} {ttb: 22h} {steam:1145360}");
        Fields.Set(e, FieldKind.Duration, Fields.MakeDuration(20, 90));
        Assert.Equal("Hades - PC {42 h} {ttb: 20h / 90h} {steam:1145360}", EntryLine.Format(e));
        Fields.Set(e, FieldKind.Duration, null);
        Assert.Equal("Hades - PC {42 h} {steam:1145360}", EntryLine.Format(e));
    }

    [Fact]
    public void Md_ida_y_vuelta()
    {
        var doc = Doc();
        var md = DocumentService.Serialize(doc, FileKind.Md);
        Assert.Contains("⌛ ttb: 9h / 25h", md);
        Assert.Contains("⌛ ttb: 100h", md);

        var back = DocumentService.Parse(md, FileKind.Md);
        var titles = doc.Sections.SelectMany(s => s.Entries).Select(e => (e.Title, Fields.Duration(e))).ToList();
        var titlesBack = back.Sections.SelectMany(s => s.Entries).Select(e => (e.Title, Fields.Duration(e))).ToList();
        Assert.Equal(titles, titlesBack);

        // .md → .txt: la línea vuelve a ser la del formato original
        var txt = DocumentService.Serialize(back, FileKind.Txt);
        Assert.Contains("-Resident Evil Village - PC {ttb: 9h / 25h}\r\n", txt);
        Assert.Contains("-Dark Souls III - PC\r\n", txt);
    }

    [Fact]
    public void Md_escrito_a_mano_con_reloj_de_arena()
    {
        var doc = DocumentService.Parse("# Lista\n\n## Por jugar\n\n- [ ] **Hollow Knight** — `PC` · ⌛ ttb: 27h / 60h\n", FileKind.Md);
        var e = doc.Sections.SelectMany(s => s.Entries).Single();
        Assert.Equal("Hollow Knight", e.Title);
        Assert.Equal((27.0, (double?)60.0), Fields.Duration(e));
    }

    // ───────────────────────────── ¿Qué juego ahora? ─────────────────────────────

    [Theory]
    [InlineData(null, GameLength.Any)]
    [InlineData(0.5, GameLength.Short)]
    [InlineData(7.9, GameLength.Short)]
    [InlineData(8.0, GameLength.Medium)]
    [InlineData(20.0, GameLength.Medium)]
    [InlineData(20.5, GameLength.Long)]
    public void Tramos_de_duracion(double? hours, GameLength expected) => Assert.Equal(expected, NextGame.LengthOf(hours));

    [Fact]
    public void Sin_filtros_primero_las_sagas_empezadas_y_luego_lo_corto()
    {
        var picks = NextGame.Suggest(Doc(), new NextFilter());
        Assert.Equal(new[]
        {
            "Resident Evil Village",   // 2 completados de su saga
            "Final Fantasy XII",       // 1 completado (saga por el diccionario: está en «(Indies)»)
            "Persona 5 Royal",         // 1 empezado
            "Portal 2", "Metroid Dread", // por duración, de menos a más
            "Dark Souls III"           // sin duración al final
        }, picks.Select(p => p.Title));

        var re = picks[0];
        Assert.Equal("Resident Evil", re.Saga);
        Assert.Equal(2, re.SagaDone);
        Assert.Equal(0, re.SagaStarted);
        Assert.Equal(new[] { "Resident Evil 4 (Remake)", "Resident Evil 7" }, re.SagaTitles);
        Assert.Equal((9.0, (double?)25.0), (re.Hours!.Value, re.FullHours));
        Assert.Equal(GameLength.Medium, re.Length);
        Assert.Equal("Por jugar", re.Section);

        Assert.Equal("Final Fantasy", picks[1].Saga);
        Assert.Equal(1, picks[2].SagaStarted);
        Assert.Null(picks.Single(p => p.Title == "Metroid Dread").Saga);   // ninguna entrega jugada
    }

    [Fact]
    public void Filtra_por_tiempo_disponible()
    {
        string[] T(GameLength l) => NextGame.Suggest(Doc(), new NextFilter(l)).Select(p => p.Title).ToArray();
        Assert.Equal(new[] { "Portal 2" }, T(GameLength.Short));
        Assert.Equal(new[] { "Resident Evil Village", "Metroid Dread" }, T(GameLength.Medium));
        Assert.Equal(new[] { "Persona 5 Royal" }, T(GameLength.Long));
    }

    [Fact]
    public void Filtra_por_sagas_empezadas()
    {
        var picks = NextGame.Suggest(Doc(), new NextFilter(SagaOnly: true));
        Assert.Equal(new[] { "Resident Evil Village", "Final Fantasy XII", "Persona 5 Royal" }, picks.Select(p => p.Title));
    }

    [Fact]
    public void Filtra_por_plataforma_y_combina_filtros()
    {
        Assert.Equal(new[] { "Persona 5 Royal", "Metroid Dread" },
            NextGame.Suggest(Doc(), new NextFilter(Platform: "switch")).Select(p => p.Title));
        Assert.Equal(new[] { "Persona 5 Royal" },
            NextGame.Suggest(Doc(), new NextFilter(GameLength.Long, SagaOnly: true, Platform: "Switch")).Select(p => p.Title));
        Assert.Empty(NextGame.Suggest(Doc(), new NextFilter(GameLength.Short, SagaOnly: true)));
    }

    [Fact]
    public void Plataformas_multiples_y_saga_por_diccionario()
    {
        var doc = DocumentService.Parse(
            "Lista\r\n\r\n-Resident Evil 2 - PS1\r\n\r\n<Por jugar>\r\n\r\n-Resident Evil 3 (Remake) - PC/PS5 {ttb: 6h}\r\n", FileKind.Txt);
        var p = Assert.Single(NextGame.Suggest(doc, new NextFilter(Platform: "PS5", SagaOnly: true)));
        Assert.Equal("Resident Evil", p.Saga);
        Assert.Equal(1, p.SagaDone);
    }

    [Fact]
    public void Sin_seccion_por_jugar_no_hay_sugerencias()
    {
        var doc = DocumentService.Parse("Lista\r\n\r\n-Portal 2 - PC {ttb: 6h}\r\n", FileKind.Txt);
        Assert.Empty(NextGame.Suggest(doc, new NextFilter()));
    }

    [Fact]
    public void Horas_estimadas_de_la_seccion()
    {
        var porJugar = Doc().Sections.Single(s => s.Kind == SectionKind.Backlog);
        Assert.Equal((123.0, 4, 6), NextGame.EstimatedHours(porJugar));
    }
}
