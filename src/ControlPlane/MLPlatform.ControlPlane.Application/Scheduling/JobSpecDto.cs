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

