using System.Security.Cryptography;

namespace ListaJuegos.Core.Sync;

/// <summary>
/// Vigila el archivo abierto y avisa (una sola vez por ráfaga) cuando su contenido cambia fuera de la app:
/// OneDrive, Syncthing, otro editor… Compara el contenido (SHA-256), no la fecha, así que no avisa de:
/// las escrituras propias (envueltas en <see cref="OwnWrite"/>), los .bak y los temporales, ni de los
/// cambios que dejan el archivo igual (copias de la sincronización que solo tocan la fecha).
/// </summary>
public sealed class FileChangeWatcher : IDisposable
{
    private readonly FileSystemWatcher _fsw;
    private readonly Timer _timer;
    private readonly TimeSpan _debounce;
    private readonly object _lock = new();
    private string? _known;
    private int _own, _retries;
    private bool _disposed;

    public string FilePath { get; }

    /// <summary>Se lanza en un hilo del sistema cuando el contenido en disco ya no es el conocido.</summary>
    public event Action<FileChangeWatcher>? Changed;

    public FileChangeWatcher(string path, TimeSpan? debounce = null)
    {
        FilePath = Path.GetFullPath(path);
        _debounce = debounce ?? TimeSpan.FromMilliseconds(600);
        _known = Hash();
        _timer = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
        // Se vigila la carpeta entera: OneDrive y Syncthing escriben un temporal y lo renombran sobre el archivo.
        _fsw = new FileSystemWatcher(Path.GetDirectoryName(FilePath)!)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            InternalBufferSize = 16 * 1024
        };
        _fsw.Changed += (_, e) => OnEvent(e.FullPath);
        _fsw.Created += (_, e) => OnEvent(e.FullPath);
        _fsw.Renamed += (_, e) => OnEvent(e.FullPath);
        _fsw.Error += (_, _) => Schedule();
        _fsw.EnableRaisingEvents = true;
    }

    /// <summary>¿Afecta este evento al archivo vigilado? Los .bak, los temporales y los demás archivos no.</summary>
    public static bool IsRelevant(string target, string changed)
    {
        if (string.IsNullOrEmpty(changed)) return false;
        var ext = Path.GetExtension(changed);
        if (ext.Equals(".bak", StringComparison.OrdinalIgnoreCase) || ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase)) return false;
        try { return string.Equals(Path.GetFullPath(changed), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private void OnEvent(string fullPath)
    {
        if (IsRelevant(FilePath, fullPath)) Schedule();
    }

    private void Schedule()
    {
        lock (_lock) { if (!_disposed) { _retries = 0; _timer.Change(_debounce, Timeout.InfiniteTimeSpan); } }
    }

    /// <summary>Toma el contenido actual del disco como el conocido (tras abrir o recargar).</summary>
    public void Acknowledge()
    {
        lock (_lock) _known = Hash();
    }

    /// <summary>Envuelve un guardado de la app: mientras dura no se avisa y al terminar su contenido pasa a ser el conocido.</summary>
    public IDisposable OwnWrite()
    {
        lock (_lock) _own++;
        return new Scope(this);
    }

    private sealed class Scope(FileChangeWatcher w) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            lock (w._lock) { w._known = w.Hash(); w._own--; }
        }
    }

    /// <summary>Compara ya el disco con lo conocido; avisa y devuelve true si ha cambiado. (El temporizador la llama tras la pausa.)</summary>
    public bool Check()
    {
        string? now;
        lock (_lock)
        {
            if (_disposed) return false;
            if (_own > 0) { _timer.Change(_debounce, Timeout.InfiniteTimeSpan); return false; }
            now = Hash(out var busy);
            // Aún lo está escribiendo otro programa: se reintenta un rato (unos 30 s) y luego se espera al siguiente evento.
            if (busy) { if (++_retries < 50) _timer.Change(_debounce, Timeout.InfiniteTimeSpan); return false; }
            _retries = 0;
            // Borrado (o a medio sustituir): se espera a que vuelva a aparecer.
            if (now == null || now == _known) return false;
            _known = now;
        }
        Changed?.Invoke(this);
        return true;
    }

    private string? Hash() => Hash(out _);

    private string? Hash(out bool busy)
    {
        busy = false;
        try
        {
            if (!File.Exists(FilePath)) return null;
            using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(fs));
        }
        catch (IOException) { busy = true; return null; }
        catch (UnauthorizedAccessException) { busy = true; return null; }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _fsw.EnableRaisingEvents = false;
        _fsw.Dispose();
        _timer.Dispose();
    }
}
