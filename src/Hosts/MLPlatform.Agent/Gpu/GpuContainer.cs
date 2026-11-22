namespace MLPlatform.Agent.Gpu;

/// <summary>A running container that holds GPUs (Docker device requests, or the mlp.gpus label under
/// MLP_FAKE_GPU).</summary>
public sealed record GpuContainer(string Id, string Name, string Owner, string Attempt, string Role, IReadOnlyList<string> Gpus)
{
    /// <summary>Ray node and network spoke containers reserve GPUs for workloads but do not use them.</summary>
    public bool IsInfrastructure => Role is "ray-node" or "net";

    public IReadOnlyDictionary<string, string> OwnerLabels =>
        Owner.Length > 0 ? new Dictionary<string, string> { [HostOwnership.Label] = Owner } : new Dictionary<string, string>();
}
