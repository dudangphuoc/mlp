using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace MLPlatform.Recipes;

/// <summary>
/// YAML → JsonNode using only YamlDotNet's event parser (no reflection serializer → AOT-safe).
/// Unquoted scalars are typed per the YAML 1.2 core schema: null/bool/int/float; everything else is a string.
/// </summary>
public static class YamlJson
{
    public static JsonNode? Parse(string yaml)
    {
        var parser = new Parser(new StringReader(yaml));
        parser.Consume<StreamStart>();
        if (parser.Accept<StreamEnd>(out _)) return null;
        parser.Consume<DocumentStart>();
        var node = ReadNode(parser);
        parser.Consume<DocumentEnd>();
        return node;
    }

    private static JsonNode? ReadNode(IParser p)
    {
        if (p.TryConsume<Scalar>(out var s)) return ScalarNode(s);
        if (p.TryConsume<SequenceStart>(out _))
        {
            var arr = new JsonArray();
            while (!p.TryConsume<SequenceEnd>(out _)) arr.Add(ReadNode(p));
            return arr;
        }
        if (p.TryConsume<MappingStart>(out _))
        {
            var obj = new JsonObject();
            while (!p.TryConsume<MappingEnd>(out _))
            {
                var key = p.Consume<Scalar>().Value;
                obj[key] = ReadNode(p);
            }
            return obj;
        }
        if (p.TryConsume<AnchorAlias>(out var a))
            throw new YamlException(a.Start, a.End, "YAML aliases are not supported in manifests");
        throw new YamlException("Unsupported YAML structure");
    }

    private static JsonNode? ScalarNode(Scalar s)
    {
        if (s.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded)
            return JsonValue.Create(s.Value);
        var v = s.Value;
        switch (v)
        {
            case "" or "~" or "null" or "Null" or "NULL": return null;
            case "true" or "True" or "TRUE": return JsonValue.Create(true);
            case "false" or "False" or "FALSE": return JsonValue.Create(false);
        }
        if (long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return JsonValue.Create(l);
        if ((char.IsDigit(v[0]) || v[0] is '-' or '+' or '.') &&
            double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return JsonValue.Create(d);
        return JsonValue.Create(v);
    }
}
