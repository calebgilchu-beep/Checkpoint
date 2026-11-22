namespace ListaJuegos.Core.Cli;

public enum CliCommand { None, Help, Add, Complete, Stats }

/// <summary>Argumentos de la línea de comandos ya interpretados. <see cref="Error"/> lleva el mensaje si son incorrectos.</summary>
public sealed record CliOptions
{
    public CliCommand Command { get; init; }
    public string? Title { get; init; }
    public string? Platform { get; init; }
    public string? Section { get; init; }
    public string? Saga { get; init; }
    public string? File { get; init; }
    public bool Json { get; init; }
    public string? Error { get; init; }

    /// <summary>¿Hay que trabajar en consola? (si no, se abre la ventana como siempre).</summary>
    public bool IsCli => Command != CliCommand.None || Error != null;
}

/// <summary>
/// Lee los argumentos de Checkpoint.exe. Sin argumentos, o con solo la ruta de una lista (doble clic, «Abrir con»,
/// arrastrar sobre el .exe), el resultado es <see cref="CliCommand.None"/> y la app abre su ventana como siempre.
/// Admite «--opción valor» y «--opción=valor».
/// </summary>
public static class CliParser
{
    private static readonly string[] Valued = { "--add", "--complete", "--platform", "--section", "--saga", "--file" };
    private static readonly string[] Flags = { "--stats", "--json", "--help", "-h", "-?", "/?", "/h", "/help" };

    /// <summary>¿Parece una orden de consola? Solo si algún argumento es una opción («--…», «-h», «/?»).</summary>
    public static bool LooksLikeCli(IReadOnlyList<string> args) =>
        args.Any(a => a.StartsWith("--", StringComparison.Ordinal) || Flags.Contains(a, StringComparer.OrdinalIgnoreCase));

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        if (!LooksLikeCli(args)) return new CliOptions();

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loose = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            var name = a;
            string? value = null;
            var eq = a.StartsWith("--", StringComparison.Ordinal) ? a.IndexOf('=') : -1;
            if (eq > 2) { name = a[..eq]; value = a[(eq + 1)..]; }

            if (Valued.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (value == null)
                {
                    if (i + 1 >= args.Count || IsOption(args[i + 1])) return Fail($"Falta el valor de {name}.");
                    value = args[++i];
                }
                if (values.ContainsKey(name)) return Fail($"{name} aparece dos veces.");
                values[name] = value.Trim();
            }
            else if (Flags.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (value != null) return Fail($"{name} no lleva valor.");
                flags.Add(name.ToLowerInvariant() switch { "-h" or "-?" or "/?" or "/h" or "/help" => "--help", var n => n });
            }
            else if (IsOption(a)) return Fail($"Opción desconocida: {a}");
            else loose.Add(a);
        }

        if (flags.Contains("--help")) return new CliOptions { Command = CliCommand.Help };

        var commands = new List<CliCommand>();
        if (values.ContainsKey("--add")) commands.Add(CliCommand.Add);
        if (values.ContainsKey("--complete")) commands.Add(CliCommand.Complete);
        if (flags.Contains("--stats")) commands.Add(CliCommand.Stats);
        if (commands.Count == 0) return Fail("Falta la orden: --add, --complete, --stats o --help.");
        if (commands.Count > 1) return Fail("Solo se puede dar una orden cada vez (--add, --complete o --stats).");
        var cmd = commands[0];

        // Una ruta suelta vale como --file (por ejemplo: Checkpoint.exe lista.txt --stats)
        var file = values.GetValueOrDefault("--file");
        if (loose.Count > 1 || (loose.Count == 1 && file != null)) return Fail($"Argumento de más: {loose[^1]}");
        file ??= loose.FirstOrDefault();

        var title = cmd switch { CliCommand.Add => values["--add"], CliCommand.Complete => values["--complete"], _ => null };
        if (title != null && title.Length == 0) return Fail("El título está vacío.");

        if (cmd != CliCommand.Add)
            foreach (var only in new[] { "--platform", "--section", "--saga" })
                if (values.ContainsKey(only)) return Fail($"{only} solo se usa con --add.");
        if (cmd == CliCommand.Add && string.IsNullOrWhiteSpace(values.GetValueOrDefault("--platform")))
            return Fail("--add necesita --platform \"<plataforma>\".");
        if (flags.Contains("--json") && cmd != CliCommand.Stats) return Fail("--json solo se usa con --stats.");
        if (file is { Length: 0 }) return Fail("La ruta de --file está vacía.");

        return new CliOptions
        {
            Command = cmd,
            Title = title,
            Platform = values.GetValueOrDefault("--platform"),
            Section = NullIfEmpty(values.GetValueOrDefault("--section")),
            Saga = NullIfEmpty(values.GetValueOrDefault("--saga")),
            File = file,
            Json = flags.Contains("--json")
        };
    }

    private static bool IsOption(string a) =>
        a.StartsWith("--", StringComparison.Ordinal) || Flags.Contains(a, StringComparer.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static CliOptions Fail(string message) => new() { Error = message };

    public const string Help =
        """
        Checkpoint — lista de juegos en .txt o .md

        Uso:
          Checkpoint.exe                         Abre la ventana.
          Checkpoint.exe "<lista.txt|.md>"       Abre esa lista en la ventana.
          Checkpoint.exe <orden> [opciones]      Trabaja en la consola, sin ventana.

        Órdenes:
          --add "<título>" --platform "<plataforma>" [--section "<sección>"] [--saga "<saga>"]
              Añade un juego. Por defecto va a «Por jugar» y, sin --saga, a los juegos sueltos.
              Si la saga no existe en esa sección, se crea («Nombre:»).
          --complete "<título>"
              Mueve el juego a Completados, a su misma saga, con la fecha de hoy si no tenía.
              Vale el título exacto o una parte que solo coincida con un juego.
          --stats [--json]
              Juegos y horas por sección, total de horas y nota media. --json da JSON para scripts.
          --help
              Esta ayuda.

        Opciones:
          --file "<ruta>"   Lista con la que trabajar. Si no se da, la última abierta en Checkpoint.

        Al guardar se respeta el formato del archivo (las líneas que no cambian quedan igual), se deja una copia .bak
        si está activada en Ajustes y se pone al día «(Actualizado el …)» si esa opción está activada.

        Códigos de salida: 0 bien · 1 error al leer o guardar · 2 argumentos incorrectos · 3 juego no encontrado,
        ambiguo o repetido.

        Ejemplos:
          Checkpoint.exe --add "Hollow Knight: Silksong" --platform "Switch 2" --saga "Hollow Knight"
          Checkpoint.exe --complete "silksong"
          Checkpoint.exe --stats --json --file "D:\Listas\Juegos que me he pasado.txt"
        """;
}
