using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ListaJuegos.Core.Parsing;

namespace ListaJuegos.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public sealed record SaveResult(string Path, string EncodingName, bool EncodingUpgraded);

public static class DocumentService
{
    static DocumentService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static FileKind KindFromPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown" or ".mdown" ? FileKind.Md : FileKind.Txt;

    public static GameDocument Parse(string text, FileKind kind) =>
        kind == FileKind.Md ? MdFormat.Parse(text) : TxtFormat.Parse(text);

    public static GameDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (text, enc, bom) = Decode(bytes);
        var doc = Parse(text, KindFromPath(path));
        doc.Layout.EncodingName = enc;
        doc.Layout.Bom = bom;
        return doc;
    }

    public static string Serialize(GameDocument doc, FileKind kind) =>
        kind == FileKind.Md ? MdFormat.Write(doc) : TxtFormat.Write(doc);

    /// <summary>Guarda de forma segura (archivo temporal + reemplazo) y opcionalmente deja una copia .bak.</summary>
    public static SaveResult Save(GameDocument doc, string path, bool backup)
    {
        var kind = KindFromPath(path);
        var text = Serialize(doc, kind);
        var (bytes, encName, upgraded) = Encode(text, doc.Layout);

        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        if (backup && File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tmp, bytes);
        if (File.Exists(path)) File.Replace(tmp, path, null, ignoreMetadataErrors: true);
        else File.Move(tmp, path);
        if (upgraded) { doc.Layout.EncodingName = encName; doc.Layout.Bom = false; }
        return new SaveResult(path, encName, upgraded);
    }

    public static GameDocument NewTemplate(DateTime today)
    {
        var doc = new GameDocument
        {
            Header = $"Lista de los juegos que me he pasado (Actualizado el {today:dd/MM/yyyy})",
            HasHeader = true
        };
        doc.Sections.Add(new Section { Name = SectionKinds.DefaultFirstSection, Implicit = true });
        doc.Sections.Add(new Section { Name = "Inconclusos" });
        doc.Sections.Add(new Section { Name = "Por jugar" });
        return doc;
    }

    public static void TouchHeaderDate(GameDocument doc, DateTime today)
    {
        if (doc.HasHeader && doc.Header.Trim().Length > 0) doc.Header = TxtFormat.UpdateHeaderDate(doc.Header, today);
    }

    public static (string text, string encoding, bool bom) Decode(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return (Encoding.UTF8.GetString(b, 3, b.Length - 3), "utf-8", true);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return (Encoding.Unicode.GetString(b, 2, b.Length - 2), "utf-16", true);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return (Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2), "utf-16BE", true);
        try
        {
            return (new UTF8Encoding(false, true).GetString(b), "utf-8", false);
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.GetEncoding(1252).GetString(b), "windows-1252", false);
        }
    }

    public static (byte[] bytes, string encoding, bool upgraded) Encode(string text, TextLayout lay)
    {
        Encoding enc = lay.EncodingName switch
        {
            "utf-16" => new UnicodeEncoding(false, false),
            "utf-16BE" => new UnicodeEncoding(true, false),
            "windows-1252" => Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback),
            _ => new UTF8Encoding(false)
        };
        try
        {
            var body = enc.GetBytes(text);
            if (!lay.Bom) return (body, lay.EncodingName, false);
            var pre = lay.EncodingName == "utf-8" ? new byte[] { 0xEF, 0xBB, 0xBF } : enc.GetPreamble();
            if (pre.Length == 0) pre = lay.EncodingName == "utf-16BE" ? new byte[] { 0xFE, 0xFF } : new byte[] { 0xFF, 0xFE };
            return (pre.Concat(body).ToArray(), lay.EncodingName, false);
        }
        catch (EncoderFallbackException)
        {
            // El texto tiene caracteres (★, emojis…) que el ANSI original no admite: se guarda en UTF-8.
            return (new UTF8Encoding(false).GetBytes(text), "utf-8", true);
        }
    }
}
