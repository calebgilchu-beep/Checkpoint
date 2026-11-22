using System.Text;

namespace ListaJuegos.Core.Connectors;

/// <summary>Lector mínimo del formato KeyValues de Valve (.vdf/.acf).</summary>
public static class Vdf
{
    public sealed class Node : Dictionary<string, object>
    {
        public Node() : base(StringComparer.OrdinalIgnoreCase) { }
        public string? Str(string key) => TryGetValue(key, out var v) ? v as string : null;
        public Node? Child(string key) => TryGetValue(key, out var v) ? v as Node : null;
    }

    public static Node Parse(string text)
    {
        var i = 0;
        var root = new Node();
        ParseInto(root, text, ref i);
        return root;
    }

    private static void ParseInto(Node node, string s, ref int i)
    {
        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return;
            if (s[i] == '}') { i++; return; }
            var key = ReadToken(s, ref i);
            if (key == null) return;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '{')
            {
                i++;
                var child = new Node();
                ParseInto(child, s, ref i);
                node[key] = child;
            }
            else
            {
                var val = ReadToken(s, ref i) ?? "";
                node[key] = val;
            }
        }
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i])) { i++; continue; }
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
            break;
        }
    }

    private static string? ReadToken(string s, ref int i)
    {
        if (i >= s.Length) return null;
        var sb = new StringBuilder();
        if (s[i] == '"')
        {
            i++;
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length) { i++; sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] }); }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }
        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}' && s[i] != '"') sb.Append(s[i++]);
        return sb.Length > 0 ? sb.ToString() : null;
    }
}
