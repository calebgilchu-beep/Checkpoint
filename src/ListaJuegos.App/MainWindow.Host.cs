using Microsoft.Win32;

namespace ListaJuegos.App;

/// <summary>Diálogos y utilidades de Windows que pide el Bridge (IHostUi).</summary>
public partial class MainWindow : IHostUi
{
    private static string Filter(FileFilter[] filters) => string.Join("|", filters.Select(f =>
        f.Exts is ["*"] ? f.Name + "|*.*"
                        : $"{f.Name} ({string.Join("; ", f.Exts.Select(e => "*." + e))})|{string.Join(";", f.Exts.Select(e => "*." + e))}"));

    public string? PickOpen(string title, FileFilter[] filters, string initialDir)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = Filter(filters), InitialDirectory = initialDir };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    public string? PickSave(string title, FileFilter[] filters, int filterIndex, string defaultExt, string fileName, string initialDir)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            Filter = Filter(filters),
            FilterIndex = filterIndex,
            DefaultExt = defaultExt,
            AddExtension = true,
            FileName = fileName,
            InitialDirectory = initialDir,
            OverwritePrompt = true
        };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    public string CoverUrl(string file) => $"https://{CoversHost}/{Uri.EscapeDataString(file)}";

    public void OpenUrl(string url) => OpenExternal(url);
}
