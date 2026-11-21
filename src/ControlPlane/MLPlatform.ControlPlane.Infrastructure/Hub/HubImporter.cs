using MLPlatform.ControlPlane.Application.Common;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>HF import worker (one job at a time: bandwidth + CPU): downloads each file from HF and PUTs it straight to
/// S3 storage via an internal signed URL (streamed, no temp storage).</summary>
public sealed class HubImporter(IServiceScopeFactory scopes, HfHub hub, SecretBox secrets, S3Client s3, IHttpClientFactory http, IConfiguration cfg,
    SystemHealth health, ILogger<HubImporter> log) : BackgroundService, IHubImportQueue
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    public void Enqueue(Guid id) => _queue.Writer.TryWrite(id);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new FailStaleHubImportsCommand(), stop) is > 0 and var n)
                log.LogWarning("{N} unfinished HF import(s) marked failed (api restarted)", n);
        }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Could not mark unfinished imports as failed"); }
        health.Beat("hub-import", 0);
        await foreach (var id in _queue.Reader.ReadAllAsync(stop))
        {
            health.Beat("hub-import", 0);
            try { await RunAsync(id, stop); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "HF import {Id} failed", id);
                await UpdateAsync(id, "failed", null, 0, 0, 0, 0, e is AppException ae ? ae.Message : e.Message);
            }
        }
    }

    private async Task UpdateAsync(Guid id, string state, Guid? target, int ft, int fd, long bt, long bd, string msg)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new UpdateHubImportCommand(id, state, target, ft, fd, bt, bd, msg));
    }

    /// <summary>One file to import: relative path, size, and where to read it from.</summary>
    private sealed record SourceFile(string Path, long Size, string Url);

    private async Task RunAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        var job = await m.Query(new GetHubImportJobQuery(id), ct);
        for (var i = 0; job is null && i < 20; i++)
        {
            await Task.Delay(500, ct);
            job = await m.Query(new GetHubImportJobQuery(id), ct);
        }
        if (job is not { State: "queued" }) return;
        scope.ServiceProvider.GetRequiredService<CurrentUser>().ActAs(job.CreatedBy);
        var token = job.Source == "hf" ? secrets.Open(await m.Query(new GetHubSecretQuery(job.WorkspaceId), ct)) : null;
        await UpdateAsync(id, "running", job.TargetId, 0, 0, 0, 0, "Listing files");

        string sourceRef;
        List<SourceFile> files;
        var gated = false;
        switch (job.Source)
        {
            case "url":
                files = await ListUrlsAsync(job.Include, ct);
                sourceRef = job.Include.Length == 1 ? job.Include[0] : $"{job.Include.Length} URLs";
                break;
            case "s3":
                var (bucket0, prefix0) = ParseS3(job.Repo);
                files = (await s3.ListAsync(bucket0, prefix0, ct)).Where(o => !o.Key.EndsWith('/'))
                    .Select(o => new SourceFile(o.Key[prefix0.Length..].TrimStart('/'), o.Size, $"s3://{bucket0}/{o.Key}"))
                    .Where(f => f.Path.Length > 0 && HfHub.Match(f.Path, job.Include)).ToList();
                sourceRef = job.Repo;
                break;
            default:
                var info = await hub.InfoAsync(job.Kind, job.Repo, job.Revision, token, ct);
                var sha = info["sha"]?.ToString() is { Length: > 0 } s ? s : job.Revision;
                gated = info["gated"] is JsonValue gv && (gv.TryGetValue<bool>(out var gb) ? gb : gv.ToString() is "auto" or "manual") || info["private"]?.GetValue<bool>() == true;
                files = (await hub.TreeAsync(job.Kind, job.Repo, sha, token, ct)).Where(f => HfHub.Match(f.Path, job.Include))
                    .Select(f => new SourceFile(f.Path, f.Size, hub.ResolveUrl(job.Kind, job.Repo, sha, f.Path))).ToList();
                sourceRef = $"hf://{(job.Kind == "dataset" ? "datasets/" : "")}{job.Repo}@{sha}";
                break;
        }
        if (files.Count == 0) { await UpdateAsync(id, "failed", null, 0, 0, 0, 0, "No files match the filter"); return; }
        var total = files.Sum(f => f.Size);

        if (job.Mode == "reference")
        {
            if (gated) { await UpdateAsync(id, "failed", null, files.Count, 0, total, 0, "This repo is gated/private — import it with \"Copy to storage\" instead"); return; }
            var refs = files.Select(f => (f.Path, f.Size, f.Url)).ToList();
            Guid target;
            if (job.Kind == "model")
            {
                var (versionId, _) = await m.Send(new ReserveHubModelVersionCommand(job.WorkspaceId, job.TargetName, job.Task, sourceRef, SourceKind(job.Source)), ct);
                await m.Send(new SetModelVersionReferenceCommand(versionId, refs), ct);
                target = versionId;
            }
            else
            {
                await m.Send(new CreateReferenceVersionCommand(job.WorkspaceId, job.TargetId!.Value, "Reference to " + sourceRef, sourceRef, refs), ct);
                target = job.TargetId!.Value;
            }
            await UpdateAsync(id, "succeeded", target, files.Count, files.Count, total, 0,
                job.Kind == "model" ? "Model version created (reference — downloaded when used)" : "Dataset version created (reference — downloaded when used)");
            log.LogInformation("Reference import {Source} done: {N} file(s)", sourceRef, files.Count);
            return;
        }

        var max = long.TryParse(cfg["Hub:MaxImportBytes"], out var mx) ? mx : 50L * 1024 * 1024 * 1024;
        if (total > max) { await UpdateAsync(id, "failed", null, files.Count, 0, total, 0, $"The source is {total / 1073741824.0:0.0} GiB, over the {max / 1073741824} GiB limit; filter out some files or use a reference import"); return; }
        string bucket, prefix;
        Guid? targetId = job.TargetId;
        if (job.Kind == "model")
        {
            var (versionId, p) = await m.Send(new ReserveHubModelVersionCommand(job.WorkspaceId, job.TargetName, job.Task, sourceRef, SourceKind(job.Source)), ct);
            (bucket, prefix, targetId) = (Buckets.Artifacts, p + "weights/", versionId);
        }
        else (bucket, prefix) = (Buckets.Datasets, Buckets.Raw(job.WorkspaceId, job.TargetId!.Value));
        await UpdateAsync(id, "running", targetId, files.Count, 0, total, 0, "Copying");

        long done = 0;
        var n = 0;
        var put = http.CreateClient("s3-put");
        foreach (var f in files)
        {
            var key = prefix + Buckets.SafePath(f.Path);
            if (f.Url.StartsWith("s3://", StringComparison.Ordinal))
            {
                var (sb, sk) = ParseS3(f.Url);
                await s3.CopyAsync(sb, sk, bucket, key, ct);
            }
            else
            {
                using var src = job.Source == "hf"
                    ? await hub.DownloadUrlAsync(f.Url, token, ct)
                    : await http.CreateClient("import").GetAsync(f.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                src.EnsureSuccessStatusCode();
                var url = s3.Presign(HttpMethod.Put, bucket, key, TimeSpan.FromHours(6), Audience.Internal);
                using var body = new StreamContent(await src.Content.ReadAsStreamAsync(ct));
                body.Headers.ContentLength = src.Content.Headers.ContentLength ?? f.Size;
                using var res = await put.PutAsync(url, body, ct);
                if (!res.IsSuccessStatusCode) throw new IOException($"S3 storage returned {(int)res.StatusCode} while writing {f.Path}");
            }
            if (job.Kind == "dataset") await m.Send(new CompleteUploadCommand(job.WorkspaceId, job.TargetId!.Value, f.Path), ct);
            done += f.Size; n++;
            await UpdateAsync(id, "running", targetId, files.Count, n, total, done, f.Path);
        }
        if (job.Kind == "model")
        {
            var root = prefix[..^"weights/".Length];
            var lockJson = new JsonObject
            {
                ["recipe"] = "hf-auto", ["variant"] = "hub-model", ["source"] = SourceKind(job.Source), ["ref"] = sourceRef,
                ["imported_at"] = TimeProvider.System.GetUtcNow().ToString("o", CultureInfo.InvariantCulture), ["files"] = files.Count,
            };
            await s3.PutAsync(Buckets.Artifacts, root + "recipe.lock.json", Encoding.UTF8.GetBytes(lockJson.ToJsonString()), "application/json", ct);
            await m.Send(new FinishHubModelVersionCommand(targetId!.Value, files.Count + 1, total), ct);
        }
        else await m.Send(new CreateVersionCommand(job.WorkspaceId, job.TargetId!.Value, "Imported from " + sourceRef), ct);
        await UpdateAsync(id, "succeeded", targetId, files.Count, n, total, done, job.Kind == "model" ? "Model version created" : "Dataset version committed");
        log.LogInformation("Import {Source} done: {N} file(s), {Bytes} bytes", sourceRef, n, done);
    }

    private static string SourceKind(string source) => source switch { "url" => "url", "s3" => "s3", _ => "hf_hub" };

    private static (string Bucket, string Key) ParseS3(string uri)
    {
        var rest = uri["s3://".Length..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? (rest, "") : (rest[..slash], rest[(slash + 1)..]);
    }

    /// <summary>File names + sizes of URL sources (HEAD; size 0 when the server does not say).</summary>
    private async Task<List<SourceFile>> ListUrlsAsync(string[] urls, CancellationToken ct)
    {
        var client = http.CreateClient("import");
        var used = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<SourceFile>();
        foreach (var u in urls)
        {
            long size = 0;
            try
            {
                using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, u), ct);
                if (!head.IsSuccessStatusCode) throw new NotFoundException("IMPORT_URL_UNREACHABLE", $"{u} returned {(int)head.StatusCode}");
                size = head.Content.Headers.ContentLength ?? 0;
            }
            catch (HttpRequestException e) { throw new NotFoundException("IMPORT_URL_UNREACHABLE", $"Cannot reach {u}: {e.Message}"); }
            var name = Uri.UnescapeDataString(new Uri(u).AbsolutePath.TrimEnd('/').Split('/')[^1]);
            if (name.Length == 0) name = "file";
            var unique = name;
            for (var i = 2; !used.Add(unique); i++) unique = $"{System.IO.Path.GetFileNameWithoutExtension(name)}-{i}{System.IO.Path.GetExtension(name)}";
            list.Add(new SourceFile(unique, size, u));
        }
        return list;
    }
}
