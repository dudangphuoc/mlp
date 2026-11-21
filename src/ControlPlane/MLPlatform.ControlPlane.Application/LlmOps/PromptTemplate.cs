using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.LlmOps;

/// <summary>Prompt template: variables `{{name}}` (letters, digits, underscores).</summary>
public static partial class PromptTemplate
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}")] private static partial Regex Var();

    public static List<string> Variables(params string[] texts) => texts.SelectMany(t => Var().Matches(t).Select(m => m.Groups[1].Value)).Distinct().ToList();

    public static string Render(string template, IReadOnlyDictionary<string, string> vars)
    {
        var missing = Var().Matches(template).Select(m => m.Groups[1].Value).Where(v => !vars.ContainsKey(v)).Distinct().ToList();
        if (missing.Count > 0) throw new InvalidArgumentException("LLM_PROMPT_VARIABLE_MISSING", "Missing variables: " + string.Join(", ", missing));
        return Var().Replace(template, m => vars[m.Groups[1].Value]);
    }
}
