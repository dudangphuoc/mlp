namespace MLPlatform.Agent;

/// <summary>Containers carry the host id of the agent that created them, so agents sharing one Docker daemon never
/// report each other's containers.</summary>
public static class HostOwnership
{
    public const string Label = "mlp.agent";

    public static string? HostId { get; set; }

    /// <summary>True for the default install, which adopts containers created before ownership labels existed.</summary>
    public static bool ClaimsUnlabeled { get; set; } = true;

    public static bool Owns(IReadOnlyDictionary<string, string> labels) =>
        HostId is null || (labels.TryGetValue(Label, out var owner) ? owner == HostId : ClaimsUnlabeled);

    public static bool IsForeign(IReadOnlyDictionary<string, string> labels) =>
        labels.TryGetValue(Label, out var owner) && owner.Length > 0 && owner != HostId;
}
