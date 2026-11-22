using System.Globalization;
using System.Text.RegularExpressions;

namespace ListaJuegos.Core;

public enum FieldKind { Rating, Hours, Achievements, StoreId, KeyValue, Tag, Duration }

/// <summary>Campos {..} de un juego: {★ 9/10}, {42 h}, {Logros 100%}, {steam:123}, {ra:1234}, {ttb: 12h / 35h},
/// {clave: valor}, {etiqueta}.</summary>
public static partial class Fields
{
    public static readonly string[] Stores = { "steam", "xbox", "epic", "gog", "psn", "nintendo", "ea", "ubisoft", "ra" };

    [GeneratedRegex(@"^★\s*(\d+(?:[.,]\d+)?)\s*/\s*10$")] private static partial Regex RatingRx();
    [GeneratedRegex(@"^(\d+(?:[.,]\d+)?)\s*h$", RegexOptions.IgnoreCase)] private static partial Regex HoursRx();
    [GeneratedRegex(@"^Logros\s*(\d+(?:[.,]\d+)?)\s*%$", RegexOptions.IgnoreCase)] private static partial Regex AchRx();
    [GeneratedRegex(@"^(steam|xbox|epic|gog|psn|nintendo|ea|ubisoft|ra):\s*(\S+)$", RegexOptions.IgnoreCase)] private static partial Regex StoreRx();
    [GeneratedRegex(@"^([^:]{1,40}):\s*(.+)$")] private static partial Regex KvRx();
    // Duración estimada («time to beat»): {ttb: 12h} o {ttb: 12h / 35h} (historia principal / completista).
    [GeneratedRegex(@"^ttb\s*:\s*(\d+(?:[.,]\d+)?)\s*h?(?:\s*/\s*(\d+(?:[.,]\d+)?)\s*h?)?$", RegexOptions.IgnoreCase)] private static partial Regex TtbRx();

    public static FieldKind Classify(string f)
    {
        f = f.Trim();
        if (RatingRx().IsMatch(f)) return FieldKind.Rating;
        if (HoursRx().IsMatch(f)) return FieldKind.Hours;
        if (AchRx().IsMatch(f)) return FieldKind.Achievements;
        if (StoreRx().IsMatch(f)) return FieldKind.StoreId;
        if (TtbRx().IsMatch(f)) return FieldKind.Duration;
        if (KvRx().IsMatch(f)) return FieldKind.KeyValue;
        return FieldKind.Tag;
    }

    public static string? StoreId(GameEntry e, string store)
    {
        foreach (var f in e.Fields)
        {
            var m = StoreRx().Match(f.Trim());
            if (m.Success && m.Groups[1].Value.Equals(store, StringComparison.OrdinalIgnoreCase)) return m.Groups[2].Value;
        }
        return null;
    }

    public static (string store, string id)? FirstStore(GameEntry e)
    {
        foreach (var f in e.Fields)
        {
            var m = StoreRx().Match(f.Trim());
            if (m.Success) return (m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value);
        }
        return null;
    }

    /// <summary>Todas las tiendas enlazadas del juego, en orden ({steam:…}, {ra:…}…).</summary>
    public static IEnumerable<(string store, string id)> AllStores(GameEntry e)
    {
        foreach (var f in e.Fields)
        {
            var m = StoreRx().Match(f.Trim());
            if (m.Success) yield return (m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value);
        }
    }

    public static double? Number(GameEntry e, FieldKind kind)
    {
        foreach (var f in e.Fields)
        {
            var t = f.Trim();
            var m = kind switch
            {
                FieldKind.Rating => RatingRx().Match(t),
                FieldKind.Hours => HoursRx().Match(t),
                FieldKind.Achievements => AchRx().Match(t),
                _ => Match.Empty
            };
            if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;
        }
        return null;
    }

    /// <summary>Duración estimada de un campo «ttb: 12h / 35h», o null si no lo es.</summary>
    public static (double main, double? full)? ParseDuration(string field)
    {
        var m = TtbRx().Match(field.Trim());
        if (!m.Success) return null;
        static double N(string s) => double.Parse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        return (N(m.Groups[1].Value), m.Groups[2].Success ? N(m.Groups[2].Value) : null);
    }

    /// <summary>Duración estimada del juego (historia principal y, si la hay, completista).</summary>
    public static (double main, double? full)? Duration(GameEntry e)
    {
        foreach (var f in e.Fields)
            if (ParseDuration(f) is { } d) return d;
        return null;
    }

    /// <summary>«ttb: 12h» o «ttb: 12h / 35h».</summary>
    public static string MakeDuration(double main, double? full = null) =>
        full is { } c && c > 0 ? $"ttb: {FormatNumber(main)}h / {FormatNumber(c)}h" : $"ttb: {FormatNumber(main)}h";

    public static string FormatNumber(double v) =>
        Math.Abs(v - Math.Round(v)) < 0.05 ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
                                           : v.ToString("0.#", CultureInfo.GetCultureInfo("es-ES"));

    public static string Make(FieldKind kind, double v) => kind switch
    {
        FieldKind.Rating => $"★ {FormatNumber(v)}/10",
        FieldKind.Hours => $"{FormatNumber(v)} h",
        FieldKind.Achievements => $"Logros {FormatNumber(v)}%",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Pone o sustituye el campo de ese tipo (o lo quita con null), manteniendo la posición.</summary>
    public static void Set(GameEntry e, FieldKind kind, string? value)
    {
        var i = e.Fields.FindIndex(f => Classify(f) == kind && (kind != FieldKind.StoreId || SameStore(f, value)));
        if (value == null) { if (i >= 0) e.Fields.RemoveAt(i); return; }
        if (i >= 0) e.Fields[i] = value; else e.Fields.Add(value);
    }

    public static void SetStore(GameEntry e, string store, string id) =>
        Set(e, FieldKind.StoreId, $"{store.ToLowerInvariant()}:{id}");

    private static bool SameStore(string field, string? value)
    {
        if (value == null) return true;
        var a = StoreRx().Match(field.Trim());
        var b = StoreRx().Match(value.Trim());
        return a.Success && b.Success && a.Groups[1].Value.Equals(b.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
    }

    public static string? StoreUrl(string store, string id) => store.ToLowerInvariant() switch
    {
        "steam" => $"https://store.steampowered.com/app/{id}",
        "xbox" when Regex.IsMatch(id, "^[A-Za-z0-9]{12}$") => $"https://www.microsoft.com/store/productId/{id}",
        "ra" when Regex.IsMatch(id, @"^\d+$") => $"https://retroachievements.org/game/{id}",
        // Epic y GOG guardan ids internos que no sirven para formar el enlace de la tienda.
        _ => null
    };

    public static (string store, string id)? FromStoreUrl(string url)
    {
        var m = Regex.Match(url, @"store\.steampowered\.com/app/(\d+)");
        if (m.Success) return ("steam", m.Groups[1].Value);
        m = Regex.Match(url, @"store\.epicgames\.com/(?:[a-z-]+/)?p/([^/?#]+)");
        if (m.Success) return ("epic", m.Groups[1].Value);
        m = Regex.Match(url, @"(?:microsoft\.com/store/productId/|xbox\.com/(?:[a-z-]+/)?games/store/[^/]+/)([A-Za-z0-9]{12})", RegexOptions.IgnoreCase);
        if (m.Success) return ("xbox", m.Groups[1].Value);
        m = Regex.Match(url, @"gog\.com/(?:[a-z]+/)?game/([^/?#]+)");
        if (m.Success) return ("gog", m.Groups[1].Value);
        m = Regex.Match(url, @"retroachievements\.org/game/(\d+)", RegexOptions.IgnoreCase);
        if (m.Success) return ("ra", m.Groups[1].Value);
        return null;
    }
}
