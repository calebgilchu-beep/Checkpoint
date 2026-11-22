using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ListaJuegos.Core.Cli;

namespace ListaJuegos.App;

/// <summary>
/// Punto de entrada. Antes de crear nada de WPF mira los argumentos: si son una orden de consola (--add, --complete,
/// --stats, --help) la ejecuta sin ventana y termina; si no (nada, o solo la ruta de una lista), abre la ventana
/// exactamente como antes.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var o = CliParser.Parse(args);
        if (!o.IsCli)
        {
            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }
        return RunConsole(o);
    }

    private static int RunConsole(CliOptions o)
    {
        ConnectConsole();
        var output = Console.Out;
        var error = Console.Error;
        try
        {
            var s = AppSettings.Load();
            var ctx = new CliContext
            {
                DefaultFile = s.Recent.FirstOrDefault(File.Exists),
                Backup = s.Backup,
                UpdateDate = s.UpdateDate,
                Today = DateTime.Now
            };
            return CliRunner.Run(o, ctx, output, error);
        }
        finally
        {
            output.Flush();
            error.Flush();
        }
    }

    // Checkpoint.exe es una app de ventana (no abre consola al hacer doble clic). Para escribir en la consola desde la que
    // se lanza se engancha a la de su proceso padre; si la salida va a un archivo o a otro programa (scripts, Stream Deck)
    // se usa esa redirección tal cual.
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int nStdHandle);
    [DllImport("kernel32.dll")] private static extern int GetFileType(IntPtr handle);
    private const int AttachParentProcess = -1, StdOutput = -11;

    private static void ConnectConsole()
    {
        var h = GetStdHandle(StdOutput);
        // Redirigida solo si va a un archivo (FILE_TYPE_DISK = 1) o a otro programa (FILE_TYPE_PIPE = 3).
        var redirected = h != IntPtr.Zero && h != new IntPtr(-1) && GetFileType(h) is 1 or 3;
        if (!redirected && AttachConsole(AttachParentProcess))
        {
            // Tras engancharse hay que volver a abrir las salidas estándar de .NET sobre la consola.
            var utf8 = new UTF8Encoding(false);
            Console.OutputEncoding = utf8;
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
            Console.WriteLine();   // la consola ya ha pintado su indicador: se empieza en una línea nueva
            return;
        }
        // Redirigida: UTF-8 sin BOM, para que «★», tildes y comillas lleguen bien a scripts y a archivos.
        var enc = new UTF8Encoding(false);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), enc) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), enc) { AutoFlush = true });
    }
}
