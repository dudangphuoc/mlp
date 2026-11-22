using System.Text.Json.Nodes;

namespace MLPlatform.ControlPlane.Application.Audit;

/// <summary>Per-request structured details a handler attaches to its audit record (path, size, sha256, …).</summary>
public sealed class AuditDetails
{
    private JsonObject? _details;

    public void Set(string key, JsonNode? value) => (_details ??= [])[key] = value;

    /// <summary>The details as compact JSON (null when none were set), clearing them for the next audited
    /// command.</summary>
    public string? Take()
    {
        var json = _details?.ToJsonString();
        _details = null;
        return json;
    }
}
