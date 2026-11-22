using System.Text;
using ListaJuegos.Core;
using ListaJuegos.Core.Parsing;

namespace ListaJuegos.Tests;

public class FormatTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "ejemplo.txt");

    [Fact]
    public void Ejemplo_real_ida_y_vuelta_byte_a_byte()
    {
        var original = File.ReadAllBytes(FixturePath);
        var tmp = Path.Combine(Path.GetTempPath(), $"lj_{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(tmp, original);
        try
        {
            var doc = DocumentService.Load(tmp);
            DocumentService.Save(doc, tmp, backup: false);
            Assert.Equal(original, File.ReadAllBytes(tmp));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Ejemplo_real_estructura()
    {
        var doc = DocumentService.Load(FixturePath);
        Assert.StartsWith("Lista de los juegos que me he pasado xdxd", doc.Header);
        Assert.Equal(new[] { "Completados", "Inclonclusos", "Por jugar" }, doc.Sections.Select(s => s.Name));
        Assert.True(doc.Sections[0].Implicit);
        Assert.Equal(SectionKind.Unfinished, doc.Sections[1].Kind);
        Assert.Equal(SectionKind.Backlog, doc.Sections[2].Kind);
        Assert.Equal(16, doc.Sections[0].Entries.Count());
        Assert.Equal(13, doc.Sections[1].Entries.Count());
        Assert.Equal(9, doc.Sections[2].Entries.Count());

        var indies = doc.Sections[0].Blocks.Single(b => b.Name == "Indies");
        Assert.Equal(BlockStyle.Paren, indies.Style);
        var divider = doc.Sections[0].Blocks.Last();
        Assert.Equal(BlockType.Divider, divider.Type);
        Assert.Equal(BlockStyle.Bracket, divider.Style);

        var re3 = doc.Sections[0].Entries.Single(e => e.Title == "Resident Evil 3 (Remake)");
        Assert.Equal("PC", re3.Platforms);
        Assert.Equal("lo he pasado de pequeño", re3.Note);
        Assert.Equal(NotePosition.AfterPlatform, re3.NotePos);

        var smb = doc.Sections[1].Entries.Single(e => e.Title == "Super Mario Bros");
        Assert.Equal("emulación", smb.Note);
        Assert.Equal(NotePosition.BeforePlatform, smb.NotePos);
        Assert.Equal("GBA/SNES/NES", smb.Platforms);

        var p4 = doc.Sections[0].Entries.Single(e => e.Title == "Persona 4 Golden");
        Assert.Null(p4.Platforms);
        Assert.Equal("29/10/2026", p4.Date);
    }

    [Fact]
    public void Editar_un_juego_solo_cambia_su_linea()
    {
        var text = File.ReadAllText(FixturePath);
        var doc = TxtFormat.Parse(text);
        var e = doc.Sections[0].Entries.First(x => x.Title == "Undertale");
        e.Date = "01/11/2026";
        e.Fields.Add("★ 10/10");
        var outText = TxtFormat.Write(doc);
        var a = text.Split("\r\n");
        var b = outText.Split("\r\n");
        Assert.Equal(a.Length, b.Length);
        var diffs = a.Zip(b).Where(p => p.First != p.Second).ToList();
        Assert.Single(diffs);
        Assert.Equal("-Undertale - PS Vita #01/11/2026# {★ 10/10}", diffs[0].Second);
    }

    [Theory]
    [InlineData("Resident Evil 4 (Remake) - PC #10/07/2026#", "Resident Evil 4 (Remake)", "PC", null, "10/07/2026")]
    [InlineData("Metal Gear Solid V: The Phantom Pain PC/Steam", "Metal Gear Solid V: The Phantom Pain PC/Steam", null, null, null)]
    [InlineData("Death Stranding 1 - Xbox PC ", "Death Stranding 1", "Xbox PC", null, null)]
    [InlineData("Halo - Reach - Xbox [coop] #1/2/2025# {★ 8/10} {12 h}", "Halo - Reach", "Xbox", "coop", "1/2/2025")]
    [InlineData("Cuphead + DLC - Nintendo Switch", "Cuphead + DLC", "Nintendo Switch", null, null)]
    public void Lineas_de_juego(string body, string title, string? plat, string? note, string? date)
    {
        var e = EntryLine.Parse(body);
        Assert.Equal(title, e.Title);
        Assert.Equal(plat, e.Platforms);
        Assert.Equal(note, e.Note);
        Assert.Equal(date, e.Date);
    }

    [Fact]
    public void Campos_con_llaves()
    {
        var e = EntryLine.Parse("Hades - PC #05/05/2025# {★ 9/10} {42 h} {Logros 100%} {Dificultad: Difícil} {steam:1145360} {coop}");
        Assert.Equal(new[] { "★ 9/10", "42 h", "Logros 100%", "Dificultad: Difícil", "steam:1145360", "coop" }, e.Fields);
        Assert.Equal(9, Fields.Number(e, FieldKind.Rating));
        Assert.Equal(42, Fields.Number(e, FieldKind.Hours));
        Assert.Equal(100, Fields.Number(e, FieldKind.Achievements));
        Assert.Equal("1145360", Fields.StoreId(e, "steam"));
        Assert.Equal(FieldKind.KeyValue, Fields.Classify("Dificultad: Difícil"));
        Assert.Equal(FieldKind.Tag, Fields.Classify("coop"));
        Assert.Equal("Hades - PC #05/05/2025# {★ 9/10} {42 h} {Logros 100%} {Dificultad: Difícil} {steam:1145360} {coop}", EntryLine.Format(e));

        Fields.Set(e, FieldKind.Hours, Fields.Make(FieldKind.Hours, 43.5));
        Assert.Equal("43,5 h", e.Fields[1]);
    }

    [Fact]
    public void Txt_a_md_y_vuelta_sin_perdidas()
    {
        var text = File.ReadAllText(FixturePath);
        var doc = TxtFormat.Parse(text);
        foreach (var opts in new[]
                 {
                     new MarkdownOptions(),
                     new MarkdownOptions { Collapsible = true, Covers = true, Emojis = false, Counts = false, SummaryTable = false, TaskList = false, Rules = false },
                     new MarkdownOptions { FrontMatter = false }
                 })
        {
            doc.Markdown = opts;
            var md = MdFormat.Write(doc);
            var back = MdFormat.Parse(md);
            var txt = TxtFormat.Write(back);
            // Mismo contenido; solo se pierden espacios sobrantes al final de línea.
            static string Norm(string s) => string.Join("\n", s.TrimEnd().Split("\r\n").Select(l => l.TrimEnd()));
            Assert.Equal(Norm(text), Norm(txt));
            // md → md estable
            Assert.Equal(md, MdFormat.Write(back));
        }
    }

    [Fact]
    public void Md_con_campos_enlaces_y_abandonados()
    {
        var doc = new GameDocument { Header = "Mis juegos (Actualizado el 01/01/2026)" };
        var s = new Section { Name = "Abandonados" };
        var b = new Block { Name = "Varios" };
        b.Entries.Add(new GameEntry
        {
            Title = "Juego *raro* [x] #1", Platforms = "PC/Steam", Note = "nota_1", NotePos = NotePosition.BeforePlatform,
            Date = "02/02/2026", Fields = { "★ 7/10", "10 h", "Logros 45%", "Modo: Difícil", "steam:620" }
        });
        b.Entries.Add(new GameEntry { Title = "Sin plataforma", Note = "algo", Fields = { "sgdb:5245268", "xbox:9PK087LNGJC5" } });
        s.Blocks.Add(b);
        doc.Sections.Add(s);
        doc.Markdown = new MarkdownOptions { Covers = true, Collapsible = true };

        var md = MdFormat.Write(doc);
        Assert.Contains("~~[**Juego \\*raro\\* \\[x\\] #1**](https://store.steampowered.com/app/620)~~ *nota\\_1* — `PC/Steam`", md);
        Assert.Contains("<details open>", md);
        Assert.Contains("library_600x900.jpg", md);

        var back = MdFormat.Parse(md);
        var e0 = back.Sections[0].Entries.First();
        Assert.Equal("Juego *raro* [x] #1", e0.Title);
        Assert.Equal("PC/Steam", e0.Platforms);
        Assert.Equal("nota_1", e0.Note);
        Assert.Equal(NotePosition.BeforePlatform, e0.NotePos);
        Assert.Equal("02/02/2026", e0.Date);
        Assert.Equal(new[] { "★ 7/10", "10 h", "Logros 45%", "Modo: Difícil", "steam:620" }, e0.Fields);
        var e1 = back.Sections[0].Entries.Last();
        Assert.Equal(new[] { "sgdb:5245268", "xbox:9PK087LNGJC5" }, e1.Fields);   // el id de SteamGridDB sobrevive al .md
        Assert.Equal("Mis juegos (Actualizado el 01/01/2026)", back.Header);
        Assert.False(back.Sections[0].Implicit);
    }

    [Fact]
    public void Md_escrito_a_mano()
    {
        var md = "# Juegos\n\n## Pasados\n\n### Zelda\n\n- Breath of the Wild - Switch #01/01/2020#\n* [x] **Tears of the Kingdom** — `Switch`\n\nTexto suelto\n";
        var doc = MdFormat.Parse(md);
        Assert.Equal("Juegos", doc.Header);
        var es = doc.Sections[0].Entries.ToList();
        Assert.Equal("Breath of the Wild", es[0].Title);
        Assert.Equal("Switch", es[0].Platforms);
        Assert.Equal("01/01/2020", es[0].Date);
        Assert.Equal("Tears of the Kingdom", es[1].Title);
        Assert.Equal(BlockStyle.Plain, doc.Sections[0].Blocks.Last().Style);
    }

    [Fact]
    public void Codificaciones_y_saltos_de_linea()
    {
        var text = "Lista\n\nRPG:\n\n-Ñandú - PC\n";
        foreach (var (enc, bom) in new[] { ("utf-8", true), ("utf-8", false), ("windows-1252", false), ("utf-16", true) })
        {
            var doc = TxtFormat.Parse(text);
            doc.Layout.EncodingName = enc; doc.Layout.Bom = bom;
            var (bytes, name, up) = DocumentService.Encode(TxtFormat.Write(doc), doc.Layout);
            Assert.False(up);
            var (dec, dname, dbom) = DocumentService.Decode(bytes);
            Assert.Equal(text, dec);
            Assert.Equal(enc, dname);
            Assert.Equal(bom, dbom);
        }

        var ansi = TxtFormat.Parse(text);
        ansi.Layout.EncodingName = "windows-1252";
        ansi.Sections[0].Entries.First().Fields.Add("★ 9/10");
        var (_, n2, up2) = DocumentService.Encode(TxtFormat.Write(ansi), ansi.Layout);
        Assert.True(up2);
        Assert.Equal("utf-8", n2);
    }

    [Fact]
    public void Fecha_de_cabecera()
    {
        var d = new DateTime(2026, 11, 21);
        Assert.Equal("Lista xd (Actualizado el 21/11/2026)", TxtFormat.UpdateHeaderDate("Lista xd (Actualizado el 15/07/2026)", d));
        Assert.Equal("Lista (Actualizado el 21/11/2026)", TxtFormat.UpdateHeaderDate("Lista", d));
    }

    [Fact]
    public void Plantilla_nueva()
    {
        var doc = DocumentService.NewTemplate(new DateTime(2026, 11, 21));
        var txt = TxtFormat.Write(doc);
        Assert.Equal("Lista de los juegos que me he pasado (Actualizado el 21/11/2026)\r\n\r\n<Inconclusos>\r\n\r\n<Por jugar>\r\n\r\n", txt);
        var back = TxtFormat.Parse(txt);
        Assert.Equal(new[] { "Completados", "Inconclusos", "Por jugar" }, back.Sections.Select(s => s.Name));
        Assert.True(back.Sections[0].Implicit);
    }

    [Theory]
    [InlineData("Inclonclusos", SectionKind.Unfinished)]
    [InlineData("Por jugar", SectionKind.Backlog)]
    [InlineData("Completados al 100%", SectionKind.Hundred)]
    [InlineData("Jugando ahora", SectionKind.Playing)]
    [InlineData("Abandonados", SectionKind.Abandoned)]
    [InlineData("Lista de deseos", SectionKind.Wishlist)]
    [InlineData("Retro", SectionKind.Custom)]
    public void Tipos_de_seccion(string name, SectionKind kind) => Assert.Equal(kind, SectionKinds.Infer(name));

    [Fact]
    public void Json_ida_y_vuelta()
    {
        var doc = DocumentService.Load(FixturePath);
        var json = System.Text.Json.JsonSerializer.Serialize(doc, Json.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<GameDocument>(json, Json.Options)!;
        Assert.Equal(TxtFormat.Write(doc), TxtFormat.Write(back));
        Assert.Contains("\"notePos\":\"beforePlatform\"", json);
    }
}
