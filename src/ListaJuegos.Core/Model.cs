using System.Text.Json.Serialization;

namespace ListaJuegos.Core;

public enum FileKind { Txt, Md }

public enum BlockType { Category, Divider }

/// <summary>Estilo de la línea de una categoría o de un separador.</summary>
public enum BlockStyle
{
    /// <summary>Categoría «Nombre:».</summary>
    Colon,
    /// <summary>Categoría «(Nombre)», p. ej. (Indies).</summary>
    Paren,
    /// <summary>Juegos sueltos sin línea de categoría.</summary>
    None,
    /// <summary>Separador «[texto]».</summary>
    Bracket,
    /// <summary>Línea de texto libre que no encaja en el formato.</summary>
    Plain
}

/// <summary>Dónde iba la nota [..] respecto a la plataforma.</summary>
public enum NotePosition { AfterPlatform, BeforePlatform }

public enum SectionKind { Completed, Hundred, Playing, Unfinished, Backlog, Abandoned, Wishlist, Custom }

public sealed class GameDocument
{
    /// <summary>Primera línea del archivo (texto libre, puede llevar «(Actualizado el dd/mm/aaaa)»).</summary>
    public string Header { get; set; } = "";
    public bool HasHeader { get; set; } = true;
    public List<Section> Sections { get; set; } = new();
    public TextLayout Layout { get; set; } = new();
    public MarkdownOptions Markdown { get; set; } = new();
}

/// <summary>Detalles del archivo original que se respetan al guardar.</summary>
public sealed class TextLayout
{
    public FileKind SourceKind { get; set; } = FileKind.Txt;
    public string EncodingName { get; set; } = "utf-8";
    public bool Bom { get; set; }
    public string NewLine { get; set; } = "\r\n";
    public bool EndsWithNewLine { get; set; } = true;
    public int TrailingBlankLines { get; set; } = 1;
    public string EntryPrefix { get; set; } = "-";
    public int GapAfterHeader { get; set; } = 1;
    public int GapBeforeHeading { get; set; } = 1;
    public int GapAfterSection { get; set; } = 1;
    public int GapAfterCategory { get; set; } = 1;
    public int GapBetweenEntries { get; set; }
}

public sealed class MarkdownOptions
{
    public bool FrontMatter { get; set; } = true;
    public bool Emojis { get; set; } = true;
    public bool Counts { get; set; } = true;
    public bool SummaryTable { get; set; } = true;
    public bool TaskList { get; set; } = true;
    public bool StoreLinks { get; set; } = true;
    public bool Covers { get; set; }
    public bool Collapsible { get; set; }
    public bool Rules { get; set; } = true;
}

public sealed class Section
{
    public string Name { get; set; } = "";
    /// <summary>La primera sección del .txt no lleva línea «&lt;Nombre&gt;».</summary>
    public bool Implicit { get; set; }
    public string? Raw { get; set; }
    public List<Block> Blocks { get; set; } = new();

    [JsonIgnore] public SectionKind Kind => SectionKinds.Infer(Name, Implicit);
    [JsonIgnore] public IEnumerable<GameEntry> Entries => Blocks.SelectMany(b => b.Entries);
}

public sealed class Block
{
    public BlockType Type { get; set; } = BlockType.Category;
    /// <summary>Nombre de la categoría o texto del separador.</summary>
    public string Name { get; set; } = "";
    public BlockStyle Style { get; set; } = BlockStyle.Colon;
    public string? Raw { get; set; }
    public List<GameEntry> Entries { get; set; } = new();
}

public sealed class GameEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = "";
    public string? Platforms { get; set; }
    public string? Note { get; set; }
    public NotePosition NotePos { get; set; } = NotePosition.AfterPlatform;
    /// <summary>Fecha tal y como está escrita (normalmente dd/mm/aaaa).</summary>
    public string? Date { get; set; }
    /// <summary>Campos {..} en orden, sin las llaves.</summary>
    public List<string> Fields { get; set; } = new();
    public string? Raw { get; set; }

    public bool SameContent(GameEntry o) =>
        Title == o.Title && Platforms == o.Platforms && Note == o.Note &&
        (Note == null || NotePos == o.NotePos) && Date == o.Date && Fields.SequenceEqual(o.Fields);

    public GameEntry Clone() => new()
    {
        Id = Id, Title = Title, Platforms = Platforms, Note = Note, NotePos = NotePos,
        Date = Date, Fields = new List<string>(Fields), Raw = Raw
    };
}
