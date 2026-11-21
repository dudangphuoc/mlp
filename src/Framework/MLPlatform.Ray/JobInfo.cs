using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MLPlatform.Ray;

public sealed class JobInfo
{
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
}
