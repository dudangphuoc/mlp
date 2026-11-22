using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Gpu;

/// <summary>One GPU sampling pass: per-GPU readings, processes, and the yield state.</summary>
public sealed record GpuReading(GpuState State, string Error, IReadOnlyList<GpuSample> Gpus, IReadOnlyList<GpuProcess> Processes, GpuYield Yield);
