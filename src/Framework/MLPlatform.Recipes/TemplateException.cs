using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace MLPlatform.Recipes;

/// <summary>Template build error → the shim exits with code 2 and MLP_ERROR (20/08 §4).</summary>
public sealed class TemplateException(string reason, string name, string message) : Exception(message)
{
    public string Reason { get; } = reason;   // placeholder_missing | placeholder_empty | template_syntax | choice_missing
    public string Name { get; } = name;
}
