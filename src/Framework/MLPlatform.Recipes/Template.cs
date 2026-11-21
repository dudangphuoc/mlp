using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace MLPlatform.Recipes;

/// <summary>
/// Standard template grammar (cookbook 20/08 §4):
/// <c>{x}</c>, <c>{x?TEXT}</c>, <c>{x|a=T1|b=T2|*=T}</c>, <c>{{</c>/<c>}}</c>.
/// An empty/null value in bare form is an error; in <c>?</c>/<c>|</c> form it is a valid falsy value.
/// Shell: values (not static text) are quoted if they contain characters outside <c>[A-Za-z0-9_./:=@%+,-]</c>.
/// </summary>
public static class Template
{
    public delegate bool Resolver(string name, out JsonNode? value);

    public static string Render(string template, Resolver resolve, RenderMode mode = RenderMode.Shell)
    {
        var sb = new StringBuilder(template.Length + 32);
        var i = 0;
        RenderInto(sb, template, ref i, resolve, mode, stopAtClose: false, stopAtPipe: false);
        return sb.ToString();
    }

    /// <summary>List placeholder names (without rendering); used by the validator.</summary>
    public static IReadOnlyCollection<string> Names(string template)
    {
        var names = new HashSet<string>();
        Render(template, (string n, out JsonNode? v) => { names.Add(n); v = JsonValue.Create("x"); return true; });
        return names;
    }

    private static void RenderInto(StringBuilder sb, string t, ref int i, Resolver resolve, RenderMode mode,
        bool stopAtClose, bool stopAtPipe)
    {
        while (i < t.Length)
        {
            var c = t[i];
            if (c == '{' && i + 1 < t.Length && t[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
            if (c == '}' && i + 1 < t.Length && t[i + 1] == '}' && !stopAtClose) { sb.Append('}'); i += 2; continue; }
            if (c == '}' && stopAtClose) return;
            if (c == '|' && stopAtPipe && IsChoiceStart(t, i + 1)) return;
            if (c == '}') throw new TemplateException("template_syntax", "", $"Unmatched '}}' at position {i}");
            if (c != '{') { sb.Append(c); i++; continue; }

            i++; // sau '{'
            var start = i;
            while (i < t.Length && (char.IsLetterOrDigit(t[i]) || t[i] is '_' or '.' or '-')) i++;
            var name = t[start..i];
            if (name.Length == 0 || i >= t.Length)
                throw new TemplateException("template_syntax", name, $"Invalid placeholder syntax at position {start}");
            var op = t[i];
            if (!resolve(name, out var value))
                throw new TemplateException("placeholder_missing", name, $"Placeholder does not exist: {name}");

            if (op == '}')
            {
                i++;
                var text = Show(value);
                if (text.Length == 0)
                    throw new TemplateException("placeholder_empty", name, $"Placeholder is empty in bare form: {{{name}}}; use {{{name}?…}}");
                sb.Append(mode == RenderMode.Shell ? Quote(text) : text);
            }
            else if (op == '?')
            {
                i++;
                var inner = new StringBuilder();
                RenderInto(inner, t, ref i, Truthy(value) ? resolve : SkipResolver(resolve), mode, true, false);
                Expect(t, ref i, name);
                if (Truthy(value)) sb.Append(inner);
            }
            else if (op == '|')
            {
                var key = Show(value);
                string? chosen = null, fallback = null;
                var matched = false;
                while (i < t.Length && t[i] == '|')
                {
                    i++;
                    var eq = t.IndexOf('=', i);
                    if (eq < 0) throw new TemplateException("template_syntax", name, $"Choice is missing '=' in {{{name}|…}}");
                    var label = t[i..eq];
                    i = eq + 1;
                    var isMatch = label == key || (label == "*" && !matched);
                    var inner = new StringBuilder();
                    RenderInto(inner, t, ref i, isMatch ? resolve : SkipResolver(resolve), mode, true, true);
                    if (label == key) { chosen = inner.ToString(); matched = true; }
                    else if (label == "*") fallback = inner.ToString();
                }
                Expect(t, ref i, name);
                var result = chosen ?? fallback
                    ?? throw new TemplateException("choice_missing", name, $"{{{name}|…}}: no choice for value '{key}' and no '*'");
                sb.Append(result);
            }
            else throw new TemplateException("template_syntax", name, $"Invalid character '{op}' after {{{name}");
        }
        if (stopAtClose) throw new TemplateException("template_syntax", "", "Missing closing '}'");
    }

    private static bool IsChoiceStart(string t, int j)
    {
        var k = j;
        while (k < t.Length && (char.IsLetterOrDigit(t[k]) || t[k] is '_' or '.' or '-' or '*')) k++;
        return k < t.Length && k > j && t[k] == '=';
    }

    private static void Expect(string t, ref int i, string name)
    {
        if (i >= t.Length || t[i] != '}') throw new TemplateException("template_syntax", name, $"Missing closing '}}' for {{{name}…}}");
        i++;
    }

    /// <summary>Unselected branches are still parsed but do not require values (avoids false empty errors).</summary>
    private static Resolver SkipResolver(Resolver inner) => (string n, out JsonNode? v) =>
    {
        inner(n, out _);
        v = JsonValue.Create("_");
        return true;
    };

    public static bool Truthy(JsonNode? v) => v switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonObject o => o.Count > 0,
        JsonValue jv when jv.TryGetValue<bool>(out var b) => b,
        JsonValue jv when jv.TryGetValue<string>(out var s) => s.Length > 0,
        JsonValue jv when jv.TryGetValue<double>(out var d) => d != 0,
        JsonValue jv when jv.TryGetValue<long>(out var l) => l != 0,
        _ => true,
    };

    public static string Show(JsonNode? v) => v switch
    {
        null => "",
        JsonArray a => string.Join(",", a.Select(Show)),
        JsonObject o => o.ToJsonString(),
        JsonValue jv when jv.TryGetValue<bool>(out var b) => b ? "true" : "false",
        JsonValue jv when jv.TryGetValue<string>(out var s) => s,
        JsonValue jv when jv.TryGetValue<long>(out var l) => l.ToString(CultureInfo.InvariantCulture),
        JsonValue jv when jv.TryGetValue<double>(out var d) => d.ToString("R", CultureInfo.InvariantCulture),
        _ => v.ToJsonString(),
    };

    public static string Quote(string s)
    {
        foreach (var ch in s)
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or '/' or ':' or '=' or '@' or '%' or '+' or ',' or '-'))
                return "'" + s.Replace("'", "'\"'\"'") + "'";
        return s;
    }
}
