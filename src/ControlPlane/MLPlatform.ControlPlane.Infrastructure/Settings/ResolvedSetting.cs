using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Decrypted settings (plain fields + secrets combined).</summary>
public sealed record ResolvedSetting(bool Enabled, IReadOnlyDictionary<string, string> Values)
{
    public string Get(string k) => Values.TryGetValue(k, out var v) ? v : "";
}
