namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

/// <summary>An alert condition that currently holds; notified after it has held for Hold.</summary>
public sealed record GpuAlertCondition(string GpuUuid, string Kind, string Level, TimeSpan Hold, string Title);
