using System.Text;
using ListaJuegos.Core;
using ListaJuegos.Core.Parsing;
using ListaJuegos.Core.Sync;

namespace ListaJuegos.Tests;

/// <summary>Fase 3: avisos de cambios hechos fuera de la app (FileChangeWatcher). Siempre en una carpeta temporal.</summary>
public sealed class FileWatchTests : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lj-watch-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;

    public FileWatchTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "Juegos que me he pasado.txt");
        File.WriteAllText(_file, "Lista\r\n\r\n-Celeste - PC\r\n", new UTF8Encoding(true));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>Cuenta los avisos y espera a que llegue el primero (o a que pase el tiempo).</summary>
    private sealed class Counter
    {
        private int _n;
        private readonly SemaphoreSlim _sig = new(0);
        public int Count => Volatile.Read(ref _n);
        public void Hit(FileChangeWatcher _) { Interlocked.Increment(ref _n); _sig.Release(); }
        public bool Wait(int ms = 5000) => _sig.Wait(ms);
    }

    private (FileChangeWatcher w, Counter c) Start()
    {
        var w = new FileChangeWatcher(_file, Debounce);
        var c = new Counter();
        w.Changed += c.Hit;
        return (w, c);
    }

    [Fact]
    public void Avisa_de_un_cambio_externo()
    {
        var (w, c) = Start();
        using (w)
        {
            File.AppendAllText(_file, "-Hades - Switch\r\n");
            Assert.True(c.Wait(), "no llegó el aviso");
            Thread.Sleep(500);
            Assert.Equal(1, c.Count);
        }
    }

    [Fact]
    public void Una_rafaga_de_escrituras_es_un_solo_aviso()
    {
        var (w, c) = Start();
        using (w)
        {
            for (var i = 0; i < 6; i++) { File.AppendAllText(_file, $"-Juego {i} - PC\r\n"); Thread.Sleep(20); }
            Assert.True(c.Wait());
            Thread.Sleep(600);
            Assert.Equal(1, c.Count);
        }
    }

    [Fact]
    public void El_guardado_propio_no_avisa()
    {
        var (w, c) = Start();
        using (w)
        {
            var doc = DocumentService.Load(_file);
            doc.Sections[0].Blocks[0].Entries.Add(new GameEntry { Title = "Hades", Platforms = "Switch" });
            using (w.OwnWrite()) DocumentService.Save(doc, _file, backup: true);
            Assert.False(c.Wait(800), "avisó del guardado de la propia app");
            Assert.True(File.Exists(_file + ".bak"));
        }
    }

    [Fact]
    public void Los_bak_y_los_temporales_no_avisan()
    {
        var (w, c) = Start();
        using (w)
        {
            File.WriteAllText(_file + ".bak", "copia");
            File.WriteAllText(Path.Combine(_dir, ".Juegos que me he pasado.txt.123.tmp"), "temporal");
            File.WriteAllText(Path.Combine(_dir, "otra lista.txt"), "otra");
            Assert.False(c.Wait(800));
        }
        Assert.False(FileChangeWatcher.IsRelevant(_file, _file + ".bak"));
        Assert.False(FileChangeWatcher.IsRelevant(_file, Path.Combine(_dir, "x.tmp")));
        Assert.False(FileChangeWatcher.IsRelevant(_file, Path.Combine(_dir, "otra lista.txt")));
        Assert.True(FileChangeWatcher.IsRelevant(_file, _file.ToUpperInvariant()));
        Assert.False(FileChangeWatcher.IsRelevant(_file, ""));
    }

    [Fact]
    public void Tocar_el_archivo_sin_cambiarlo_no_avisa()
    {
        var (w, c) = Start();
        using (w)
        {
            File.SetLastWriteTimeUtc(_file, DateTime.UtcNow.AddMinutes(1));
            File.WriteAllBytes(_file, File.ReadAllBytes(_file));   // misma copia, como hace a veces la sincronización
            Assert.False(c.Wait(800));
        }
    }

    [Fact]
    public void Sustituir_con_un_temporal_y_renombrar_avisa()
    {
        // Así escriben OneDrive, Syncthing y muchos editores: archivo temporal + renombrar encima.
        var (w, c) = Start();
        using (w)
        {
            var tmp = Path.Combine(_dir, "~syncthing~lista.tmp");
            File.WriteAllText(tmp, "Lista\r\n\r\n-Celeste - PC\r\n-Hades - Switch\r\n", new UTF8Encoding(true));
            File.Move(tmp, _file, overwrite: true);
            Assert.True(c.Wait());
        }
    }

    [Fact]
    public void Tras_avisar_el_mismo_contenido_no_vuelve_a_avisar()
    {
        var (w, c) = Start();
        using (w)
        {
            File.AppendAllText(_file, "-Hades - Switch\r\n");
            Assert.True(c.Wait());
            // «Mantener mis cambios»: la misma versión externa no se vuelve a anunciar…
            Assert.False(w.Check());
            // …pero otro cambio distinto sí
            File.AppendAllText(_file, "-Tunic - PC\r\n");
            Assert.True(c.Wait());
            Assert.Equal(2, c.Count);
        }
    }

    [Fact]
    public void Acknowledge_acepta_la_version_actual()
    {
        var (w, c) = Start();
        using (w)
        {
            using (w.OwnWrite()) File.AppendAllText(_file, "-Hades - Switch\r\n");
            w.Acknowledge();
            Assert.False(w.Check());
            Assert.Equal(0, c.Count);
        }
    }

    [Fact]
    public void Borrar_el_archivo_no_avisa_hasta_que_vuelve()
    {
        var (w, c) = Start();
        using (w)
        {
            File.Delete(_file);
            Assert.False(c.Wait(600));
            File.WriteAllText(_file, "Lista\r\n\r\n-Otra cosa - PC\r\n");
            Assert.True(c.Wait());
        }
    }

    [Fact]
    public void Despues_de_cerrar_no_avisa()
    {
        var (w, c) = Start();
        w.Dispose();
        File.AppendAllText(_file, "-Hades - Switch\r\n");
        Assert.False(c.Wait(600));
        Assert.False(w.Check());
    }

    [Fact]
    public void La_recarga_conserva_el_archivo_byte_a_byte()
    {
        // Recargar es volver a leer: el contenido externo se respeta tal cual al guardarlo sin tocar.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Lista (Actualizado el 01/01/2026)\r\n\r\n-Celeste - PC {★ 9/10}\r\n-Hades - Switch\r\n")).ToArray();
        File.WriteAllBytes(_file, bytes);
        var doc = DocumentService.Load(_file);
        var copy = Path.Combine(_dir, "copia.txt");
        DocumentService.Save(doc, copy, backup: false);
        Assert.Equal(bytes, File.ReadAllBytes(copy));
    }
}
