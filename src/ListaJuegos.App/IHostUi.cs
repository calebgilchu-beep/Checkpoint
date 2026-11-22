namespace ListaJuegos.App;

/// <summary>Filtro de un diálogo de archivos: nombre visible y extensiones sin punto («txt», «md»…). «*» = todos.</summary>
public sealed record FileFilter(string Name, params string[] Exts);

/// <summary>Lo que el Bridge necesita de la ventana. WPF (Windows) y Photino (Linux) lo implementan.</summary>
public interface IHostUi
{
    string? PickOpen(string title, FileFilter[] filters, string initialDir);
    string? PickSave(string title, FileFilter[] filters, int filterIndex, string defaultExt, string fileName, string initialDir);
    void SetDirty(bool dirty, string title);
    void ApplyTitleBarTheme();
    void PostEvent(string name, object data);
    double Zoom { get; set; }
    bool FullScreen { get; }
    bool SetFullScreen(bool on);
    void ForceClose();
    /// <summary>URL con la que la interfaz carga una portada de la carpeta de portadas.</summary>
    string CoverUrl(string file);
    void OpenUrl(string url);
}
