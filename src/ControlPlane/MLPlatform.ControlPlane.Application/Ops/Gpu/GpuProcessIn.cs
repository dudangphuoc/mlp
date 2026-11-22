namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

/// <summary>GPU memory held by a container; Owner is the mlp.agent label (another agent's container when it
/// differs).</summary>
public sealed record GpuProcessIn(string GpuUuid, string Container, Guid? AttemptId, string Owner, long UsedBytes, bool Estimated);
