namespace MLPlatform.Agent;

/// <summary>Containers carry the host id of the agent that created them, so agents sharing one Docker daemon never report each other's containers.</summary>
public static class HostOwnership
{
    public const string Label = "mlp.agent";

    public static string? HostId { get; set; }

    public static bool Owns(IDictionary<string, string> labels) =>
        HostId is null || !labels.TryGetValue(Label, out var owner) || owner == HostId;
}
