using Grpc.Core;
using Grpc.Net.Client;
using MLPlatform.Contracts.V1;
using Environment = System.Environment;

namespace MLPlatform.Shim;

/// <summary>Sends metrics to TrackingService with the workload token (20/08 §5.1 step 6).</summary>
public sealed class TrackingSink : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly TrackingService.TrackingServiceClient _client;
    private readonly string _runId;
    private readonly Metadata _auth;
    private readonly List<Contracts.V1.MetricPoint> _pending = [];
    private readonly TextWriter _log;

    private TrackingSink(string api, string runId, string token, TextWriter log)
    {
        _channel = GrpcChannel.ForAddress(api.StartsWith("http", StringComparison.Ordinal) ? api : "http://" + api);
        _client = new TrackingService.TrackingServiceClient(_channel);
        _runId = runId;
        _auth = new Metadata { { "authorization", "Bearer " + token } };
        _log = log;
    }

    public static TrackingSink? FromEnvironment(TextWriter log)
    {
        var api = Environment.GetEnvironmentVariable("MLP_API");
        var run = Environment.GetEnvironmentVariable("MLP_RUN_ID");
        var token = Environment.GetEnvironmentVariable("MLP_JOB_TOKEN");
        return api is { Length: > 0 } && run is { Length: > 0 } && token is { Length: > 0 } ? new TrackingSink(api, run, token, log) : null;
    }

    public async Task SendAsync(IEnumerable<MetricPoint> points)
    {
        _pending.AddRange(points.Select(p => new Contracts.V1.MetricPoint { Key = p.Key, Step = p.Step, Value = p.Value }));
        if (_pending.Count == 0) return;
        if (_pending.Count > 100_000) _pending.RemoveRange(0, _pending.Count - 100_000);
        var req = new LogMetricsRequest { RunId = _runId };
        req.Points.AddRange(_pending.Take(10_000));
        try
        {
            await _client.LogMetricsAsync(req, _auth, deadline: TimeProvider.System.GetUtcNow().UtcDateTime.AddSeconds(15));
            _pending.RemoveRange(0, req.Points.Count);
        }
        catch (RpcException e)
        {
            _log.WriteLine($"[mlp-shim] sending metrics failed ({e.StatusCode}), keeping {_pending.Count} points to resend");
        }
    }

    /// <summary>Upload the model folder to S3 storage via presigned PUTs issued by the api (20/08 §5.1 step 7).</summary>
    public async Task UploadModelFolderAsync(string folder, CancellationToken ct)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).Order().ToList();
        var runtime = new RuntimeService.RuntimeServiceClient(_channel);
        var req = new CreateArtifactUploadsRequest { RunId = _runId };
        req.Paths.AddRange(files);
        var urls = await runtime.CreateArtifactUploadsAsync(req, _auth, cancellationToken: ct);
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        var done = new CompleteArtifactsRequest { RunId = _runId };
        for (var i = 0; i < files.Count; i++)
        {
            var path = Path.Combine(folder, files[i]);
            await using var fs = File.OpenRead(path);
            using var content = new StreamContent(fs);
            using var res = await http.PutAsync(urls.Urls[i], content, ct);
            if (!res.IsSuccessStatusCode) throw new ShimException(1, "artifact_upload_failed", $"Uploading {files[i]} failed with HTTP {(int)res.StatusCode}");
            done.Files.Add(new ArtifactFile { Path = files[i], SizeBytes = new FileInfo(path).Length });
        }
        await runtime.CompleteArtifactsAsync(done, _auth, cancellationToken: ct);
        _log.WriteLine($"[mlp-shim] uploaded {files.Count} model folder file(s) to storage");
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = 0; i < 3 && _pending.Count > 0; i++) await SendAsync([]);
        _channel.Dispose();
    }
}
