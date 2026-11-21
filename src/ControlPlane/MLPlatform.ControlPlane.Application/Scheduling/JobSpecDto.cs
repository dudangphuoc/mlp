using System.Text.Json;
using System.Text.Json.Serialization;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>Job spec stored in `sched.jobs.spec` (jsonb).</summary>
public sealed class JobSpecDto
{
    public string Image { get; set; } = "";
    public List<string> Command { get; set; } = [];
    public double Cpus { get; set; } = 1;
    public int Gpus { get; set; }
    /// <summary>GPU memory the job reserves on its one GPU (D-50: a GPU is shared by memory budget).</summary>
    public int GpuMemoryMb { get; set; }
    public long MemoryBytes { get; set; }
    public List<Guid> Hosts { get; set; } = [];
    public int Nnodes { get; set; } = 1;
    public Dictionary<string, string> Env { get; set; } = [];
    public string? Network { get; set; }
    public int TimeoutSeconds { get; set; } = 86_400;

    public string ToJson() => JsonSerializer.Serialize(this, SchedJson.Default.JobSpecDto);
    public static JobSpecDto Parse(string json) => JsonSerializer.Deserialize(json, SchedJson.Default.JobSpecDto) ?? new();
}

