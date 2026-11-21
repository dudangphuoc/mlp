using System.Text.Json;
using System.Text.Json.Serialization;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>What a running attempt holds on one machine — sent to the holder to reserve it again after the Ray head restarted.</summary>
public sealed class HeldBundleDto
{
    [JsonPropertyName("host_id")] public Guid HostId { get; set; }
    [JsonPropertyName("cpus")] public double Cpus { get; set; }
    [JsonPropertyName("memory_bytes")] public long MemoryBytes { get; set; }
    [JsonPropertyName("gpu_uuids")] public string[] GpuUuids { get; set; } = [];
    [JsonPropertyName("gpu_fraction")] public double GpuFraction { get; set; }
}
