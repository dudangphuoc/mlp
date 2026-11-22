namespace MLPlatform.Api.Http.HostOps;

/// <summary>Deletes terminal recordings older than 30 days, once an hour.</summary>
public sealed class TerminalRecordingJanitor(MLPlatform.Storage.S3Client s3, TimeProvider time, ILogger<TerminalRecordingJanitor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromMinutes(3), stop);
        while (!stop.IsCancellationRequested)
        {
            try { await SweepAsync(stop); }
            catch (Exception e) when (e is HttpRequestException or MLPlatform.Storage.S3Exception or TaskCanceledException && !stop.IsCancellationRequested)
            {
                log.LogWarning("Terminal recording clean-up failed: {Msg}", e.Message);
            }
            await Task.Delay(TimeSpan.FromHours(1), stop);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var bucket = MLPlatform.ControlPlane.Application.Data.Buckets.Artifacts;
        var cutoff = time.GetUtcNow() - TerminalRecordings.Retention;
        foreach (var o in (await s3.ListAsync(bucket, TerminalRecordings.Prefix, ct)).Where(o => o.LastModified < cutoff))
            await s3.DeleteAsync(bucket, o.Key, ct);
    }
}
