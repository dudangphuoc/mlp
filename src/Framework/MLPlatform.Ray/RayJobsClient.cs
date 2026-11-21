using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MLPlatform.Ray;

/// <summary>Ray Jobs REST API on the head (127.0.0.1:8265, called only by the api; 20/06 §4). JSON is an allowed exception (20/03 §1).</summary>
public sealed class RayJobsClient(HttpClient http)
{
    public async Task SubmitAsync(string submissionId, string entrypoint, IDictionary<string, string>? metadata, CancellationToken ct)
    {
        var body = new SubmitJobRequest
        {
            Entrypoint = entrypoint, SubmissionId = submissionId, EntrypointNumCpus = 0,
            Metadata = metadata is null ? null : new Dictionary<string, string>(metadata),
        };
        using var res = await http.PostAsJsonAsync("api/jobs/", body, RayJson.Default.SubmitJobRequest, ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Ray submit {submissionId}: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync(ct)}");
    }

    public async Task StopAsync(string submissionId, CancellationToken ct)
    {
        using var res = await http.PostAsync($"api/jobs/{submissionId}/stop", null, ct);
        // 404 = the job no longer exists → treat it as stopped
    }

    /// <summary>Forget a finished job so its submission id can be used again.</summary>
    public async Task DeleteAsync(string submissionId, CancellationToken ct)
    {
        using var res = await http.DeleteAsync($"api/jobs/{submissionId}", ct);
    }

    public async Task<JobInfo?> GetAsync(string submissionId, CancellationToken ct)
    {
        using var res = await http.GetAsync($"api/jobs/{submissionId}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync(RayJson.Default.JobInfo, ct);
    }

    /// <summary>GET a dashboard API path (state API, cluster status) as JSON; null when Ray does not answer.</summary>
    public async Task<System.Text.Json.Nodes.JsonNode?> GetJsonAsync(string path, CancellationToken ct)
    {
        try
        {
            using var res = await http.GetAsync(path, ct);
            if (!res.IsSuccessStatusCode) return null;
            return System.Text.Json.Nodes.JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { return null; }
    }

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        try
        {
            using var res = await http.GetAsync("api/version", ct);
            return res.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
    }
}
