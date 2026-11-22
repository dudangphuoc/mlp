using MLPlatform.ControlPlane.Application.Scheduling;
namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

public sealed record GpuHistoryData(IReadOnlyList<GpuPointRow> Points, IReadOnlyList<GpuJobMarkRow> Jobs, int StepSeconds);
