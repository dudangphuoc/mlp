using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/// <summary>Allowed values (validated on write; the UI lists them).</summary>
public static class AiCatalog
{
    public static readonly string[] ProviderTypes = ["openai-compatible", "openai", "anthropic", "deepseek", "systemone", "typesafe"];
    public static readonly string[] Roles = ["analyst", "decision", "judge", "vision", "labeler", "embed", "draft"];
    public static readonly string[] Inputs = ["text", "image", "audio", "video", "pdf"];
    public static readonly string[] Outputs = ["text", "json", "embedding", "boxes", "masks"];
    public static readonly string[] Skills = ["streaming", "tools", "structured", "reasoning"];
    public static readonly string[] Tiers = ["small", "standard", "frontier"];

    /// <summary>Decision servers (System One wire format) — only decision/judge roles make sense there.</summary>
    public static bool IsDecisionServer(string providerType) => providerType is "systemone" or "typesafe";

    /// <summary>Hosted APIs outside the platform: data sent to them leaves the platform.</summary>
    public static bool ExternalByDefault(string providerType) => providerType is "openai" or "anthropic" or "deepseek" or "typesafe";

    public static string[] Clean(IEnumerable<string> values, string[] allowed, string field)
    {
        var list = values.Select(v => v.Trim().ToLowerInvariant()).Where(v => v.Length > 0).Distinct().ToArray();
        var bad = list.Where(v => !allowed.Contains(v)).ToArray();
        if (bad.Length > 0) throw new ValidationException(new Dictionary<string, string> { [field] = $"Unknown value(s): {string.Join(", ", bad)}" });
        return list;
    }
}
