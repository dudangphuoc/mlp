using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Definition of one settings field.</summary>
public sealed record SettingFieldDef(string Key, string Label, string Type = "text", bool Required = false, string Hint = "", string[]? Options = null, string Default = "")
{
    public bool Secret => Type == "secret";
}
