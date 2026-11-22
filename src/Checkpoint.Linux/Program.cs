using System.Text;
using ListaJuegos.Core.Cli;

namespace ListaJuegos.App;

/// <summary>Punto de entrada en Linux: órdenes de consola (--add, --complete, --stats, --help) o la ventana.</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var o = CliParser.Parse(args);
        if (o.IsCli) return RunConsole(o);

        var file = args.FirstOrDefault(File.Exists);
        try
        {
            new LinuxWindow(file is null ? null : Path.GetFullPath(file)).Run();
            return 0;
        }
        catch (DllNotFoundException e)
        {
            Console.Error.WriteLine(Lang.T(
                "Checkpoint necesita WebKitGTK 4.1 y GTK 3. Instálalos (por ejemplo: sudo apt install libwebkit2gtk-4.1-0 libnotify4) y vuelve a abrir la app.",
                "Checkpoint needs WebKitGTK 4.1 and GTK 3. Install them (e.g. sudo apt install libwebkit2gtk-4.1-0 libnotify4) and reopen the app.",
                "O Checkpoint precisa do WebKitGTK 4.1 e do GTK 3. Instale-os (ex.: sudo apt install libwebkit2gtk-4.1-0 libnotify4) e abra o app de novo."));
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    private static int RunConsole(CliOptions o)
    {
        var enc = new UTF8Encoding(false);
        Console.OutputEncoding = enc;
        var output = new StreamWriter(Console.OpenStandardOutput(), enc) { AutoFlush = true };
        var error = new StreamWriter(Console.OpenStandardError(), enc) { AutoFlush = true };
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
}
