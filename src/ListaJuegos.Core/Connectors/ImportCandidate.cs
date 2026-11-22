namespace ListaJuegos.Core.Connectors;

/// <summary>Un juego encontrado en una plataforma, listo para proponer su importación.</summary>
public sealed class ImportCandidate
{
    public string Source { get; set; } = "";          // steam, xbox, epic, gog, ea, ubisoft, retroachievements, emu-<consola>
    public string Title { get; set; } = "";
    public string? StoreKey { get; set; }              // "steam", "xbox"… para el campo {tienda:id}
    public string? StoreId { get; set; }
    public double? Hours { get; set; }
    public double? Achievements { get; set; }          // porcentaje 0-100
    public DateTime? LastPlayed { get; set; }
    public bool Installed { get; set; }
    public string? Platform { get; set; }              // texto para la plataforma: "Steam", "Xbox PC"…
    public string? CoverUrl { get; set; }
    public string? LocalImage { get; set; }
    public List<string> Genres { get; set; } = new();
    public string? Note { get; set; }                   // nota propuesta: «emulación» para RetroAchievements y emuladores
    public double? Hardcore { get; set; }              // RetroAchievements: porcentaje desbloqueado en modo Hardcore
    public bool Beaten { get; set; }                   // RetroAchievements: juego «superado» (beaten)
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ContentKey { get; set; }            // RetroArch: nombre del contenido para emparejar su tiempo de juego

    // Sugerencias (las rellena ImportPlanner)
    public string? SuggestedSection { get; set; }
    public string? SuggestedCategory { get; set; }
    public bool SuggestedParen { get; set; }
    public string? DuplicateOf { get; set; }
    public string? DuplicateEntryId { get; set; }
    public bool Selected { get; set; }
}

public sealed class ConnectorResult
{
    public string Source { get; set; } = "";
    public bool Ok { get; set; } = true;
    public string? Message { get; set; }
    public List<ImportCandidate> Games { get; set; } = new();
}
