using System.Text.Json;
using ListaJuegos.Core;

namespace ListaJuegos.Tests;

/// <summary>Genera los datos de prueba de la interfaz (ui/mock) cuando se define LJ_MOCK_OUT.</summary>
public class MockExport
{
    [Fact]
    public void Exportar_datos_de_prueba()
    {
        var dir = Environment.GetEnvironmentVariable("LJ_MOCK_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var doc = DocumentService.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ejemplo.txt"));
        // Algunos campos de ejemplo para enseñar puntuaciones, horas, logros y portadas.
        var all = doc.Sections.SelectMany(s => s.Entries).ToList();
        void F(string title, params string[] fields) { var e = all.First(x => x.Title == title); e.Fields.AddRange(fields); }
        F("Resident Evil 4 (Remake)", "★ 10/10", "38 h", "Logros 72%", "steam:2050650");
        F("Persona 4 Golden", "★ 9/10", "78 h", "steam:1113000");
        F("Undertale", "★ 9/10", "steam:391540");
        F("Cuphead + DLC", "★ 8/10", "Dificultad: Difícil", "steam:268910");
        F("Resident Evil 7 (OG)", "6 h", "steam:418370");
        F("Death Stranding 1", "12 h", "Logros 18%");
        F("Doom: The Dark Ages", "4 h");
        File.WriteAllText(Path.Combine(dir, "sample.json"), JsonSerializer.Serialize(doc, Json.Options));
        File.WriteAllText(Path.Combine(dir, "sample.txt"), DocumentService.Serialize(doc, FileKind.Txt));
        File.WriteAllText(Path.Combine(dir, "sample.md"), DocumentService.Serialize(doc, FileKind.Md));
    }
}
