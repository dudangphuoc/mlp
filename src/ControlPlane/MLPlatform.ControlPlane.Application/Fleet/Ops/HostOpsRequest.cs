namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

/// <summary>A remote ops call for the agent; only the fields of its kind are used.</summary>
public sealed record HostOpsRequest(HostOpsKind Kind)
{
    public string Path { get; init; } = "";
    public string To { get; init; } = "";
    public ulong Offset { get; init; }
    public uint Length { get; init; }
    public ReadOnlyMemory<byte> Data { get; init; }
    public bool Truncate { get; init; }
    public bool Recursive { get; init; }
    public int Pid { get; init; }
    public bool Force { get; init; }
    public bool Tree { get; init; }
    public string Name { get; init; } = "";
    public string Action { get; init; } = "";
    public string Sort { get; init; } = "";
    public int Lines { get; init; }
}
