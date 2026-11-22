using ListaJuegos.Core;
using ListaJuegos.Core.Parsing;
using ListaJuegos.Core.Stats;

namespace ListaJuegos.Tests;

/// <summary>Fase 3: métricas por año, plataformas y sagas (LibraryStats).</summary>
public class StatsTests
{
    private const string Lista =
        "Lista de los juegos que me he pasado (Actualizado el 01/11/2026)\r\n\r\n" +
        "Resident Evil:\r\n\r\n" +
        "-Resident Evil 4 (Remake) - PC / Steam Deck #10/07/2026# {★ 9/10} {30 h}\r\n" +
        "-Resident Evil 7 - PS4 #01/02/2024# {★ 8/10} {12 h}\r\n" +
        "-Resident Evil 2 - pc #15/03/2026# {★ 10/10}\r\n\r\n" +
        "Final Fantasy:\r\n\r\n" +
        "-Final Fantasy X - PS2 #05/05/10#\r\n\r\n" +
        "(Indies)\r\n\r\n" +
        "-Hollow Knight - Switch #2026# {★ 9,5/10} {45 h}\r\n" +
        "-Final Fantasy VII Remake - PS5\r\n" +
        "-Celeste\r\n\r\n" +
        "<Completados al 100%>\r\n\r\n" +
        "-Astro Bot - PS5 #20/01/2026# {★ 9/10} {15 h}\r\n\r\n" +
        "<Inconclusos>\r\n\r\n" +
        "Persona:\r\n\r\n" +
        "-Persona 4 Golden - PC #01/01/2026# {20 h} {★ 6/10}\r\n\r\n" +
        "<Por jugar>\r\n\r\n" +
        "-Metroid Dread - Switch #03/03/2026#\r\n";

    private static GameDocument Doc() => TxtFormat.Parse(Lista);

    [Theory]
    [InlineData("10/07/2026", 2026, 7, 10)]
    [InlineData(" 1/2/2024 ", 2024, 2, 1)]
    [InlineData("05/05/10", 2010, 5, 5)]
    [InlineData("05.05.1999", 1999, 5, 5)]
    [InlineData("2023-11-04", 2023, 11, 4)]
    [InlineData("03/2022", 2022, 3, 0)]
    [InlineData("2021", 2021, 0, 0)]
    public void Lee_las_fechas_habituales(string date, int y, int m, int d)
    {
        Assert.Equal((y, m, d), LibraryStats.ParseDate(date));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("algún día")]
    [InlineData("32/13/2020")]
    [InlineData("1850")]
    public void Ignora_fechas_que_no_lo_son(string? date)
    {
        Assert.Null(LibraryStats.ParseDate(date));
    }

    [Fact]
    public void Totales_de_la_lista()
    {
        var st = LibraryStats.Compute(Doc());
        Assert.Equal(10, st.Total);
        Assert.Equal(8, st.Completed);          // 7 en Completados + 1 al 100 %
        Assert.Equal(6, st.Dated);
        Assert.Equal(2, st.Undated);
        Assert.Equal(30 + 12 + 45 + 15 + 20, st.Hours);   // horas de toda la lista, también las de Inconclusos
        Assert.Equal(5, st.WithHours);
        Assert.Equal(6, st.Rated);
        Assert.Equal(Math.Round((9 + 8 + 10 + 9.5 + 9 + 6) / 6.0, 2), st.Rating);
    }

    [Fact]
    public void Completados_por_año_sin_huecos()
    {
        var st = LibraryStats.Compute(Doc());
        Assert.Equal(Enumerable.Range(2010, 17), st.Years.Select(y => y.Year));
        var y2026 = st.Years.Single(y => y.Year == 2026);
        Assert.Equal(4, y2026.Games);            // RE4, RE2, Hollow Knight y Astro Bot (Persona está en Inconclusos)
        Assert.Equal(30 + 45 + 15, y2026.Hours);
        Assert.Equal(4, y2026.Rated);
        Assert.Equal(Math.Round((9 + 10 + 9.5 + 9) / 4.0, 2), y2026.Rating);
        Assert.Equal(1, st.Years.Single(y => y.Year == 2024).Games);
        Assert.Equal(0, st.Years.Single(y => y.Year == 2015).Games);
        Assert.Null(st.Years.Single(y => y.Year == 2015).Rating);
    }

    [Fact]
    public void Lista_sin_fechas_no_tiene_años()
    {
        var doc = TxtFormat.Parse("Lista\r\n\r\n-Celeste - PC\r\n-Hades - Switch {★ 9/10}\r\n");
        var st = LibraryStats.Compute(doc);
        Assert.Empty(st.Years);
        Assert.Equal(2, st.Undated);
        Assert.Equal(9, st.Rating);
        Assert.Equal(0, st.Hours);
    }

    [Fact]
    public void Plataformas_por_la_principal_y_sin_distinguir_mayusculas()
    {
        var st = LibraryStats.Compute(Doc());
        // «PC / Steam Deck» cuenta como PC; «pc» y «PC» van juntas con el nombre de la primera vez
        Assert.Equal(new[] { ("PC", 2), ("PS5", 2), ("PS2", 1), ("PS4", 1), ("Switch", 1) }, st.Platforms.Select(p => (p.Name, p.Games)));
        Assert.Equal(1, st.NoPlatform);          // Celeste
        Assert.Equal(st.Completed, st.Platforms.Sum(p => p.Games) + st.NoPlatform);
    }

    [Fact]
    public void Sagas_de_la_lista_y_del_diccionario()
    {
        var st = LibraryStats.Compute(Doc());
        var sagas = st.Sagas.ToDictionary(s => s.Name, s => s.Games);
        Assert.Equal(3, sagas["Resident Evil"]);
        // FF X por su saga «Final Fantasy:» y FF VII Remake (suelto en Indies) por el diccionario
        Assert.Equal(2, sagas["Final Fantasy"]);
        Assert.Equal("Resident Evil", st.Sagas[0].Name);
        Assert.DoesNotContain("Persona", sagas.Keys);   // solo cuentan los completados
        Assert.DoesNotContain("Indies", sagas.Keys);    // los géneros «(…)» no son sagas
    }

    [Fact]
    public void Resumen_del_año_en_orden_de_fecha()
    {
        var y = LibraryStats.Year(Doc(), 2026);
        Assert.Equal(4, y.Games);
        // Fechas completas primero (por mes y día); «#2026#» sin mes al final
        Assert.Equal(new[] { "Astro Bot", "Resident Evil 2", "Resident Evil 4 (Remake)", "Hollow Knight" }, y.List.Select(g => g.Title));
        Assert.Equal(90, y.Hours);
        Assert.Equal(Math.Round((9 + 10 + 9.5 + 9) / 4.0, 2), y.Rating);
        Assert.True(y.List[0].Hundred);
        Assert.Equal(new[] { "Resident Evil 2", "Hollow Knight", "Astro Bot" }, y.Top.Select(g => g.Title));
        Assert.Equal(new[] { 2026, 2024, 2010 }, y.Available);
        var doc = Doc();
        Assert.Equal(doc.Sections[1].Entries.Single().Id, LibraryStats.Year(doc, 2026).List[0].EntryId);
    }

    [Fact]
    public void Año_sin_juegos()
    {
        var y = LibraryStats.Year(Doc(), 2019);
        Assert.Equal(0, y.Games);
        Assert.Empty(y.List);
        Assert.Empty(y.Top);
        Assert.Null(y.Rating);
    }

    [Fact]
    public void Las_estadisticas_no_tocan_el_documento()
    {
        var doc = Doc();
        var before = TxtFormat.Write(doc);
        LibraryStats.Compute(doc);
        LibraryStats.Year(doc, 2026);
        Assert.Equal(before, TxtFormat.Write(doc));
        Assert.Equal(Lista, before);
    }

    [Fact]
    public void Funciona_igual_con_md()
    {
        var md = MdFormat.Write(Doc());
        var st = LibraryStats.Compute(MdFormat.Parse(md));
        var a = LibraryStats.Compute(Doc());
        Assert.Equal(a.Completed, st.Completed);
        Assert.Equal(a.Years.Select(y => (y.Year, y.Games)), st.Years.Select(y => (y.Year, y.Games)));
        Assert.Equal(a.Platforms.Select(p => (p.Name, p.Games)), st.Platforms.Select(p => (p.Name, p.Games)));
    }
}
