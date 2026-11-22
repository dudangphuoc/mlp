namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

/// <summary>The agent's answer: the serialized CommandResult plus the written file's hash and size when there is
/// one.</summary>
public sealed record HostOpsReply(byte[] Result, string? Sha256, long Size);
