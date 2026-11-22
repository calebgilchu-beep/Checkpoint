using System.Globalization;
using System.Text.Json;
using ListaJuegos.Core.Stats;

namespace ListaJuegos.Core.Cli;

/// <summary>Lo que la línea de comandos toma de los ajustes de la app.</summary>
public sealed record CliContext
{
    /// <summary>Lista con la que trabajar si no se da --file (la última abierta en Checkpoint).</summary>
    public string? DefaultFile { get; init; }
    /// <summary>Dejar una copia .bak antes de escribir (Ajustes › Copia de seguridad).</summary>
    public bool Backup { get; init; } = true;
    /// <summary>Poner al día «(Actualizado el …)» al guardar.</summary>
    public bool UpdateDate { get; init; } = true;
    public DateTime Today { get; init; } = DateTime.Now;
}

public static class CliExit
{
    public const int Ok = 0, Failure = 1, Usage = 2, NotFound = 3;
}

public sealed record SectionSummary(string Name, string Kind, int Games, double Hours);

public sealed record StatsSummary(string File, int Total, int Completed, double Hours, double? Rating, int Rated, List<SectionSummary> Sections);

/// <summary>Ejecuta una orden de consola sobre una lista. No usa la interfaz: lo comparten la app y las pruebas.</summary>
public static class CliRunner
{
    public static int Run(CliOptions o, CliContext ctx, TextWriter output, TextWriter error)
    {
        if (o.Error != null)
        {
            error.WriteLine($"Checkpoint: {o.Error}");
            error.WriteLine("Escribe «Checkpoint.exe --help» para ver las órdenes.");
            return CliExit.Usage;
        }
        if (o.Command == CliCommand.Help || o.Command == CliCommand.None)
        {
            output.WriteLine(CliParser.Help);
            return CliExit.Ok;
        }

        var path = o.File ?? ctx.DefaultFile;
        if (string.IsNullOrWhiteSpace(path))
        {
            error.WriteLine("Checkpoint: no hay ninguna lista abierta antes en Checkpoint. Indica una con --file \"<ruta>\".");
            return CliExit.Usage;
        }
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
        {
            error.WriteLine($"Checkpoint: no existe el archivo «{path}».");
            return CliExit.Failure;
        }

        try
        {
            var doc = DocumentService.Load(path);
            switch (o.Command)
            {
                case CliCommand.Stats:
                {
                    var s = Summarize(doc, path);
                    if (o.Json) output.WriteLine(JsonSerializer.Serialize(s, JsonOut));
                    else WriteStats(s, output);
                    return CliExit.Ok;
                }
                case CliCommand.Add:
                {
                    var x = ListEdit.Add(doc, o.Title!, o.Platform!, o.Section, o.Saga, ctx.Today);
                    Save(doc, path, ctx);
                    output.WriteLine($"Añadido «{x.Entry.Title}» ({x.Entry.Platforms}) a «{x.Section.Name}»{Where(x.Block)}.");
                    return CliExit.Ok;
                }
                case CliCommand.Complete:
                {
                    var x = ListEdit.Complete(doc, o.Title!, ctx.Today);
                    if (x == null)
                    {
                        output.WriteLine($"«{o.Title!.Trim()}» ya estaba completado. No se ha cambiado nada.");
                        return CliExit.Ok;
                    }
                    Save(doc, path, ctx);
                    output.WriteLine($"Completado «{x.Entry.Title.Trim()}»: movido a «{x.Section.Name}»{Where(x.Block)} con fecha {x.Entry.Date}.");
                    return CliExit.Ok;
                }
            }
        }
        catch (ListEditException e) { error.WriteLine($"Checkpoint: {e.Message}"); return CliExit.NotFound; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Checkpoint: no se pudo leer o guardar «{path}»: {e.Message}");
            return CliExit.Failure;
        }
        return CliExit.Usage;
    }

    private static string Where(Block b) => b.Style switch
    {
        BlockStyle.Colon => $", saga «{b.Name}»",
        BlockStyle.Paren => $", en ({b.Name})",
        _ => ""
    };

    private static void Save(GameDocument doc, string path, CliContext ctx)
    {
        if (ctx.UpdateDate) DocumentService.TouchHeaderDate(doc, ctx.Today);
        // Cada ejecución es una «sesión»: la primera (y única) escritura deja el .bak si está activado.
        DocumentService.Save(doc, path, ctx.Backup);
    }

    private static readonly JsonSerializerOptions JsonOut = new(Json.Options) { WriteIndented = true };

    public static StatsSummary Summarize(GameDocument doc, string path)
    {
        var st = LibraryStats.Compute(doc);
        var sections = doc.Sections.Select(s => new SectionSummary(
            s.Name, JsonNamingPolicy.CamelCase.ConvertName(s.Kind.ToString()), s.Entries.Count(),
            s.Entries.Sum(e => Fields.Number(e, FieldKind.Hours) ?? 0))).ToList();
        return new StatsSummary(path, st.Total, st.Completed, st.Hours, st.Rating, st.Rated, sections);
    }

    private static void WriteStats(StatsSummary s, TextWriter w)
    {
        static string N(double v) => Fields.FormatNumber(v);
        static string Games(int n) => n == 1 ? "1 juego" : $"{n} juegos";
        var width = Math.Max(10, s.Sections.Select(x => x.Name.Length).DefaultIfEmpty(0).Max()) + 2;
        w.WriteLine(Path.GetFileName(s.File));
        w.WriteLine();
        foreach (var x in s.Sections)
            w.WriteLine($"  {x.Name.PadRight(width)}{Games(x.Games),11}{(x.Hours > 0 ? $"{N(x.Hours)} h" : "—"),10}");
        w.WriteLine($"  {new string('─', width + 21)}");
        w.WriteLine($"  {"Total".PadRight(width)}{Games(s.Total),11}{$"{N(s.Hours)} h",10}");
        w.WriteLine();
        var pct = s.Total > 0 ? (s.Completed * 100.0 / s.Total).ToString("0", CultureInfo.InvariantCulture) : "0";
        w.WriteLine($"  Completados: {s.Completed} de {s.Total} ({pct} %)");
        w.WriteLine(s.Rating is { } r ? $"  Nota media: {N(r)}/10 ({Games(s.Rated)} con nota)" : "  Nota media: sin juegos puntuados");
    }
}
