using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Gpu;

/// <summary>Samples GPUs through nvidia-smi; remembers whether a GPU was ever seen so a failing driver reads as an
/// error rather than "no GPU".</summary>
public static class GpuSampler
{
    private static string _reasonsField = "clocks_event_reasons";
    private static bool _basicOnly;
    private static bool _everSeen;

    public static async Task<GpuReading> SampleAsync(AgentOptions o, CancellationToken ct)
    {
        var (state, error, gpus) = await ReadGpusAsync(ct);
        var containers = await GpuContainers.ListAsync(ct);
        var yield = YieldOf(o, containers);
        if (state != GpuState.Ok) return new GpuReading(state, error, gpus, [], yield);
        var processes = await ProcessesAsync(gpus, containers, ct);
        return new GpuReading(state, "", gpus, processes, yield);
    }

    private static async Task<(GpuState, string, List<GpuSample>)> ReadGpusAsync(CancellationToken ct)
    {
        var (code, outp, err) = await QueryAsync(ct);
        if (code == -1 && !_everSeen) return (GpuState.None, "", []);
        if (code != 0) return (_everSeen ? GpuState.DriverError : GpuState.None, FirstLine(err, outp), []);
        var gpus = GpuCsv.Samples(outp);
        if (gpus.Count == 0) return (_everSeen ? GpuState.DriverError : GpuState.None, _everSeen ? "nvidia-smi returned no GPU" : "", []);
        _everSeen = true;
        return (GpuState.Ok, "", gpus);
    }

    private static async Task<(int, string, string)> QueryAsync(CancellationToken ct)
    {
        while (true)
        {
            var fields = _basicOnly ? GpuCsv.BasicFields : GpuCsv.Fields.Replace("{reasons}", _reasonsField, StringComparison.Ordinal);
            var r = await NvidiaSmi.RunAsync(["--query-gpu=" + fields, "--format=csv,noheader,nounits"], ct);
            if (r.Code == 0 || _basicOnly || !(r.Out + r.Err).Contains("not a valid field", StringComparison.OrdinalIgnoreCase)) return r;
            if (_reasonsField == "clocks_event_reasons") _reasonsField = "clocks_throttle_reasons";
            else _basicOnly = true;
        }
    }

    private static string FirstLine(string err, string outp)
    {
        var line = (err + "\n" + outp).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "nvidia-smi failed";
        return line.Length > 240 ? line[..240] : line;
    }

    public static GpuYield YieldOf(AgentOptions o, IReadOnlyList<GpuContainer> containers)
    {
        var y = new GpuYield();
        if (!o.YieldsGpu) return y;
        y.ForeignContainers.AddRange(containers.Where(c => !c.IsInfrastructure && HostOwnership.IsForeign(c.OwnerLabels)).Select(c => c.Name));
        y.Active = y.ForeignContainers.Count > 0;
        return y;
    }

    private static async Task<List<GpuProcess>> ProcessesAsync(List<GpuSample> gpus, List<GpuContainer> containers, CancellationToken ct)
    {
        var (code, outp, _) = await NvidiaSmi.RunAsync(["--query-compute-apps=gpu_uuid,pid,used_memory", "--format=csv,noheader,nounits"], ct);
        var apps = code == 0 ? GpuCsv.Apps(outp) : [];
        var mapped = apps.Select(a => Mapped(a, containers)).ToList();
        if (mapped.Any(p => p.Container.Length > 0)) return mapped;
        return [.. mapped, .. GpuAttribution.Estimate(gpus, containers)];
    }

    private static GpuProcess Mapped((string Uuid, int Pid, ulong Bytes) a, List<GpuContainer> containers)
    {
        var p = new GpuProcess { GpuUuid = a.Uuid, Pid = a.Pid, UsedMemoryBytes = a.Bytes };
        if (Cgroup.ContainerOf(a.Pid) is { } id && containers.FirstOrDefault(c => c.Id == id) is { } c)
        {
            p.Container = c.Name;
            p.AttemptId = c.Attempt;
            p.OwnerAgent = c.Owner;
        }
        return p;
    }
}
