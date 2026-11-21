using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MLPlatform.Ray;

public sealed class SubmitJobRequest
{
    [JsonPropertyName("entrypoint")] public required string Entrypoint { get; init; }
    [JsonPropertyName("submission_id")] public required string SubmissionId { get; init; }
    [JsonPropertyName("entrypoint_num_cpus")] public double EntrypointNumCpus { get; init; }
    [JsonPropertyName("metadata")] public Dictionary<string, string>? Metadata { get; init; }
}
