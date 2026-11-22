using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Gpu;

/// <summary>Estimates GPU usage per container from its device assignment when nvidia-smi cannot report pids
/// (Windows/WSL): the GPU's used memory is split evenly between the containers assigned to it.</summary>
public static class GpuAttribution
{
    public static IEnumerable<GpuProcess> Estimate(IReadOnlyList<GpuSample> gpus, IReadOnlyList<GpuContainer> containers)
    {
        var holders = gpus.ToDictionary(g => g.Uuid, g => containers.Where(c => Holds(c, g)).ToList());
        foreach (var g in gpus)
        {
            var on = holders[g.Uuid];
            foreach (var c in on)
                yield return new GpuProcess
                {
                    GpuUuid = g.Uuid, Container = c.Name, AttemptId = c.Attempt, OwnerAgent = c.Owner,
                    UsedMemoryBytes = g.MemoryUsedBytes / (ulong)on.Count, Estimated = true,
                };
        }
    }

    public static bool Holds(GpuContainer c, GpuSample g) =>
        c.Gpus.Any(d => d == "all" || d == g.Uuid || d == g.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
