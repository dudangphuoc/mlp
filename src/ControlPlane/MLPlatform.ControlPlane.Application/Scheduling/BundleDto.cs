using System.Text.Json;
using System.Text.Json.Serialization;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed class BundleDto
{
    [JsonPropertyName("host_id")] public Guid HostId { get; set; }
    [JsonPropertyName("cpus")] public double Cpus { get; set; }
    [JsonPropertyName("gpus")] public int Gpus { get; set; }
    /// <summary>Share of one GPU to reserve (0 &lt; x &lt; 1); 0 = whole GPUs as counted by <see cref="Gpus"/>.</summary>
    [JsonPropertyName("gpu_fraction")] public double GpuFraction { get; set; }
    [JsonPropertyName("memory_bytes")] public long MemoryBytes { get; set; }
}
