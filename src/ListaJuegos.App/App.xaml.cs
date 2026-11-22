using System.IO;
using System.Windows;

namespace ListaJuegos.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // «Abrir con» o arrastrar un archivo sobre el .exe: el primer argumento es la lista que hay que abrir.
        var file = e.Args.FirstOrDefault(a => File.Exists(a));
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(Lang.T("Ha ocurrido un error inesperado:", "An unexpected error occurred:", "Ocorreu um erro inesperado:") + $"\n\n{ex.Exception.Message}", "Checkpoint", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        var w = new MainWindow(file is null ? null : Path.GetFullPath(file));
        MainWindow = w;
        w.Show();
    }
}
