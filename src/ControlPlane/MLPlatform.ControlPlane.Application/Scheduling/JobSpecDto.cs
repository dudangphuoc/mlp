using System.Text.Json;
using System.Text.Json.Serialization;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>Job spec stored in `sched.jobs.spec` (jsonb). The `recipe` field is for jobs that run a shim (M09/M17).</summary>
public sealed class JobSpecDto
{
    public string Image { get; set; } = "";
    public List<string> Command { get; set; } = [];
    public double Cpus { get; set; } = 1;
    public int Gpus { get; set; }
    /// <summary>GPU memory the job reserves on its one GPU (D-50: a GPU is shared by memory budget). 0 = derived from the job kind (see <see cref="GpuNeed"/>).</summary>
    public int GpuMemoryMb { get; set; }
    public long MemoryBytes { get; set; }
    public List<Guid> Hosts { get; set; } = [];      // pinned machines; empty → scheduler picks
    public int Nnodes { get; set; } = 1;
    public Dictionary<string, string> Env { get; set; } = [];
    public string? Network { get; set; }             // dev: docker network name; default container:mlp-net
    public int TimeoutSeconds { get; set; } = 86_400;

    public string ToJson() => JsonSerializer.Serialize(this, SchedJson.Default.JobSpecDto);
    public static JobSpecDto Parse(string json) => JsonSerializer.Deserialize(json, SchedJson.Default.JobSpecDto) ?? new();
}

public sealed class BundleDto
{
    [JsonPropertyName("host_id")] public Guid HostId { get; set; }
    [JsonPropertyName("cpus")] public double Cpus { get; set; }
    [JsonPropertyName("gpus")] public int Gpus { get; set; }
    /// <summary>Share of one GPU to reserve (0 &lt; x &lt; 1); 0 = whole GPUs as counted by <see cref="Gpus"/>.</summary>
    [JsonPropertyName("gpu_fraction")] public double GpuFraction { get; set; }
    [JsonPropertyName("memory_bytes")] public long MemoryBytes { get; set; }
}

/// <summary>What a running attempt holds on one machine — sent to the holder to reserve it again after the Ray head restarted.</summary>
public sealed class HeldBundleDto
{
    [JsonPropertyName("host_id")] public Guid HostId { get; set; }
    [JsonPropertyName("cpus")] public double Cpus { get; set; }
    [JsonPropertyName("memory_bytes")] public long MemoryBytes { get; set; }
    [JsonPropertyName("gpu_uuids")] public string[] GpuUuids { get; set; } = [];
    [JsonPropertyName("gpu_fraction")] public double GpuFraction { get; set; }
}

/// <summary>
/// D-50: one GPU runs several jobs. A job with one GPU reserves a memory budget on it instead of the whole device; the scheduler packs jobs
/// while the budgets fit. Nothing enforces the budget inside the container — it is a reservation, the same way CPU shares are.
/// The budget: what the job states, else by kind — deployment 2 GB, notebook 4 GB, recipe job = the recipe's (variant's) `minVramGb`
/// (half of it for anything that is not training). Unknown → the whole GPU, as before.
/// </summary>
public static class GpuNeed
{
    public const int ServeMb = 2048, NotebookMb = 4096;

    public static int Mb(JobSpecDto spec)
    {
        if (spec.Gpus != 1) return 0;
        if (spec.GpuMemoryMb > 0) return spec.GpuMemoryMb;
        if (spec.Env.ContainsKey("MLP_DEPLOYMENT")) return ServeMb;
        if (spec.Env.ContainsKey("MLP_NOTEBOOK")) return NotebookMb;
        if (!spec.Env.TryGetValue("MLP_JOB_SPEC", out var json)) return 0;
        try
        {
            var o = System.Text.Json.Nodes.JsonNode.Parse(json);
            var gb = Number(o?["variant"]?["minVramGb"]) ?? Number(o?["manifest"]?["environment"]?["minVramGb"]);
            if (gb is not > 0) return 0;
            var mb = (int)(gb.Value * 1024);
            return o?["entrypoint"]?.ToString() == "train" ? mb : Math.Max(ServeMb, mb / 2);
        }
        catch (JsonException) { return 0; }
    }

    /// <summary>Share of a GPU with <paramref name="gpuTotalMb"/> memory; 0 = the whole GPU (budget unknown, or it does not leave room for anything else).</summary>
    public static double Fraction(int needMb, long gpuTotalMb)
    {
        if (needMb <= 0 || gpuTotalMb <= 0) return 0;
        // Rounded down to Ray's precision (0.0001): rounding up made 2 + 8 + 2 GB on a 12 GB GPU add up to 1.001 and the third job never fit.
        var f = Math.Floor(needMb * 10000.0 / gpuTotalMb) / 10000.0;
        return f >= 0.9999 ? 0 : Math.Max(f, 0.0001);
    }

    private static double? Number(System.Text.Json.Nodes.JsonNode? n) =>
        n is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(JobSpecDto))]
[JsonSerializable(typeof(List<BundleDto>))]
[JsonSerializable(typeof(List<HeldBundleDto>))]
public sealed partial class SchedJson : JsonSerializerContext;
