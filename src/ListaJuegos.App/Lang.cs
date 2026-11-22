using System.Globalization;

namespace ListaJuegos.App;

/// <summary>Idioma de los textos nativos (diálogos de Windows). La interfaz web se traduce en ui/i18n.js.</summary>
public static class Lang
{
    /// <summary>"es", "en" o "pt". Se fija desde los ajustes ("system" = idioma de Windows).</summary>
    public static string Code { get; private set; } = "es";

    public static void Set(string? pref)
    {
        var code = string.IsNullOrEmpty(pref) || pref == "system" ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : pref;
        Code = code is "es" or "en" or "pt" ? code : "en";
    }

    public static string T(string es, string en, string pt) => Code switch { "en" => en, "pt" => pt, _ => es };
}
