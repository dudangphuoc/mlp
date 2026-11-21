using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace MLPlatform.Shim;

/// <summary>
/// Host dataset cache (see DataCacheRules on the control plane). The dataset's volume is mounted at <c>spec.DataCache</c>:
/// <c>objects/ab/{id}</c> = one immutable file per cache id, <c>views/run-{run}/…</c> = this run's dataset tree as hard links to objects.
/// Only files whose object is missing (or was modified in place — size or stamp differ) are downloaded; a dataset that did not change
/// transfers nothing. Files without an id (references that may change at the source) are downloaded into the view every time.
/// Progress is reported in bytes with a <c>sync</c> detail (reused / to download / done / failed), and a final summary the control plane keeps.
/// </summary>
public sealed partial class Runner
{
    // Every object gets this modification time when written; a recipe that rewrites a cached file in place changes it → re-downloaded next time.
    private static readonly DateTime ObjectStamp = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const long DiskMargin = 512L << 20;
    private string? _view;

    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Link(string existing, string created);

    private sealed record SyncFile(string Path, string Url, long Size, string? Id);

    private sealed class SyncState
    {
        public long ReusedBytes, FetchBytes, DoneBytes, Retries;
        public int Files, ReusedFiles, FetchFiles, DoneFiles, UncachedFiles;
        public readonly ConcurrentDictionary<string, (string Error, int Attempt)> Failed = new();
        public long PauseUntil;
    }

    private async Task SyncCachedAsync(string cache, List<SyncFile> files, CancellationToken ct)
    {
        var started = TimeProvider.System.GetTimestamp();
        var objects = Path.Combine(cache, "objects");
        var views = Path.Combine(cache, "views");
        Directory.CreateDirectory(objects);
        Directory.CreateDirectory(views);
        var runKey = Environment.GetEnvironmentVariable("MLP_RUN_ID") ?? Environment.GetEnvironmentVariable("MLP_ATTEMPT_ID") ?? Guid.NewGuid().ToString("N");
        var view = Path.Combine(views, "run-" + runKey);
        RemoveStaleViews(views, view);

        var st = new SyncState { Files = files.Count };
        var fetch = new List<SyncFile>();
        foreach (var f in files)
        {
            if (f.Id is null) { st.UncachedFiles++; fetch.Add(f); continue; }
            var obj = ObjectPath(objects, f.Id);
            var fi = new FileInfo(obj);
            if (fi.Exists && (f.Size < 0 || fi.Length == f.Size) && fi.LastWriteTimeUtc == ObjectStamp) { st.ReusedFiles++; st.ReusedBytes += fi.Length; }
            else fetch.Add(f);
        }
        st.FetchFiles = fetch.Count;
        st.FetchBytes = fetch.Sum(f => Math.Max(0, f.Size));
        var free = new DriveInfo(cache).AvailableFreeSpace;
        if (st.FetchBytes > free - DiskMargin)
            throw new ShimException(2, "data_disk_full", $"Not enough disk space for the dataset on this machine: need {Bytes(st.FetchBytes)}, free {Bytes(free)}");
        log.WriteLine($"[mlp-shim] dataset cache {cache}: {files.Count} file(s), {st.ReusedFiles} reused ({Bytes(st.ReusedBytes)}), " +
                      $"{st.FetchFiles} to download ({Bytes(st.FetchBytes)}), {st.UncachedFiles} not cacheable, free {Bytes(free)}");

        if (Directory.Exists(view)) Directory.Delete(view, recursive: true);   // retried attempt: rebuild the view (links are cheap)
        Directory.CreateDirectory(view);
        TouchAlive(view);
        _view = view;
        _ = KeepAliveAsync(view);

        using var progress = new CancellationTokenSource();
        var reporter = Task.Run(async () =>
        {
            while (!progress.IsCancellationRequested && _ops is not null)
            {
                await ReportSyncAsync(st, started, free, final: false);
                try { await Task.Delay(2000, progress.Token); } catch (OperationCanceledException) { break; }
            }
        }, CancellationToken.None);
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        try
        {
            await Parallel.ForEachAsync(fetch, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (f, token) =>
            {
                var target = f.Id is null ? ViewPath(view, f.Path) : ObjectPath(objects, f.Id);
                await DownloadOneAsync(http, f, target, st, token);
                if (f.Id is not null) File.SetLastWriteTimeUtc(target, ObjectStamp);
            });
        }
        finally
        {
            await progress.CancelAsync();
            await reporter;
        }

        foreach (var f in files.Where(f => f.Id is not null))
        {
            var dst = ViewPath(view, f.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            if (Link(ObjectPath(objects, f.Id!), dst) != 0) File.Copy(ObjectPath(objects, f.Id!), dst, overwrite: true);   // not the same file system
        }
        spec.Data = view;
        var (cacheFiles, cacheBytes) = Usage(objects);
        await ReportSyncAsync(st, started, new DriveInfo(cache).AvailableFreeSpace, final: true, cacheFiles, cacheBytes);
        log.WriteLine($"[mlp-shim] dataset ready in {view}: {files.Count} file(s), {st.ReusedFiles} reused ({Bytes(st.ReusedBytes)}), " +
                      $"{st.DoneFiles} downloaded ({Bytes(st.DoneBytes)}) in {TimeProvider.System.GetElapsedTime(started).TotalSeconds:0.0} s; cache {cacheFiles} file(s), {Bytes(cacheBytes)}");
    }

    private async Task DownloadOneAsync(HttpClient http, SyncFile f, string target, SyncState st, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var part = $"{target}.part-{Guid.NewGuid():N}";   // unique: two jobs fetching the same object never write the same file
        for (var attempt = 1; ; attempt++)
        {
            var wait = Interlocked.Read(ref st.PauseUntil) - Environment.TickCount64;
            if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), token);
            TimeSpan? backoff = null;
            long got = 0;
            try
            {
                using var res = await http.GetAsync(f.Url, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)res.StatusCode is 429 or 503)
                {
                    backoff = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Min(300, 15 * Math.Pow(2, Math.Min(attempt, 5) - 1)));
                    var until = Environment.TickCount64 + (long)backoff.Value.TotalMilliseconds;
                    long seen;
                    do { seen = Interlocked.Read(ref st.PauseUntil); } while (until > seen && Interlocked.CompareExchange(ref st.PauseUntil, until, seen) != seen);
                    throw new HttpRequestException($"HTTP {(int)res.StatusCode}");
                }
                if (!res.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)res.StatusCode}");
                await using (var src = await res.Content.ReadAsStreamAsync(token))
                await using (var fs = File.Create(part))
                {
                    var buf = new byte[81920];
                    int n;
                    while ((n = await src.ReadAsync(buf, token)) > 0)
                    {
                        await fs.WriteAsync(buf.AsMemory(0, n), token);
                        got += n;
                        Interlocked.Add(ref st.DoneBytes, n);
                    }
                }
                if (f.Size >= 0 && got != f.Size) throw new IOException($"size {got} B, expected {f.Size} B");
                File.Move(part, target, overwrite: true);
                st.Failed.TryRemove(f.Path, out _);
                Interlocked.Increment(ref st.DoneFiles);
                return;
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !token.IsCancellationRequested)
            {
                Interlocked.Add(ref st.DoneBytes, -got);
                try { File.Delete(part); } catch (IOException) { }
                st.Failed[f.Path] = (e.Message, attempt);
                Interlocked.Increment(ref st.Retries);
                if (attempt >= 10) throw new ShimException(1, "data_download_failed", $"Downloading {f.Path} failed: {e.Message}");
                if (backoff is null) await Task.Delay(TimeSpan.FromSeconds(2 * attempt), token);
            }
        }
    }

    private async Task ReportSyncAsync(SyncState st, long started, long free, bool final, long cacheFiles = -1, long cacheBytes = -1)
    {
        if (_ops is null) return;
        var failed = new JsonArray(st.Failed.Take(10).Select(kv => (JsonNode)new JsonObject
            { ["path"] = kv.Key.Length > 160 ? kv.Key[^160..] : kv.Key, ["error"] = kv.Value.Error.Length > 120 ? kv.Value.Error[..120] : kv.Value.Error, ["attempt"] = kv.Value.Attempt }).ToArray());
        var sync = new JsonObject
        {
            ["dataset"] = spec.DatasetId, ["files"] = st.Files, ["reused_files"] = st.ReusedFiles, ["reused_bytes"] = st.ReusedBytes,
            ["fetch_files"] = st.FetchFiles, ["fetch_bytes"] = st.FetchBytes, ["done_files"] = Volatile.Read(ref st.DoneFiles),
            ["done_bytes"] = Interlocked.Read(ref st.DoneBytes), ["uncached_files"] = st.UncachedFiles, ["retries"] = Interlocked.Read(ref st.Retries),
            ["failed"] = failed, ["free_bytes"] = free, ["seconds"] = Math.Round(TimeProvider.System.GetElapsedTime(started).TotalSeconds, 1), ["final"] = final,
        };
        if (cacheBytes >= 0) { sync["cache_files"] = cacheFiles; sync["cache_bytes"] = cacheBytes; }
        sync["version"] = spec.DatasetVersionId;
        sync["subset"] = spec.Trial is not null;   // a trial syncs a sample of the version, not all of it
        var wait = Interlocked.Read(ref st.PauseUntil) - Environment.TickCount64;
        await _ops.ProgressAsync("preparing_data", Interlocked.Read(ref st.DoneBytes), st.FetchBytes, "B",
            wait > 0 ? $"rate limited by the source — waiting {wait / 1000} s" : final ? "dataset ready" : "syncing dataset",
            detailJson: new JsonObject { ["sync"] = sync }.ToJsonString(), force: final);
    }

    private static string ObjectPath(string objects, string id) => Path.Combine(objects, id[..2], id);

    private static string ViewPath(string view, string path)
    {
        var target = Path.GetFullPath(Path.Combine(view, path));
        if (!target.StartsWith(Path.GetFullPath(view) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ShimException(2, "data_path_invalid", $"File path is outside the data folder: {path}");
        return target;
    }

    /// <summary>Views of runs that ended without cleaning up (container killed): their keep-alive stamp is older than a day.</summary>
    private void RemoveStaleViews(string views, string mine)
    {
        foreach (var d in Directory.EnumerateDirectories(views))
        {
            if (d == mine) continue;
            var alive = Path.Combine(d, ".alive");
            var last = File.Exists(alive) ? File.GetLastWriteTimeUtc(alive) : Directory.GetLastWriteTimeUtc(d);
            if (TimeProvider.System.GetUtcNow().UtcDateTime - last < TimeSpan.FromDays(1)) continue;
            try { Directory.Delete(d, recursive: true); log.WriteLine($"[mlp-shim] removed stale dataset view {Path.GetFileName(d)}"); }
            catch (IOException) { }
        }
    }

    private static void TouchAlive(string view) => File.WriteAllText(Path.Combine(view, ".alive"), TimeProvider.System.GetUtcNow().UtcDateTime.ToString("O"));

    private static async Task KeepAliveAsync(string view)
    {
        while (Directory.Exists(view))
        {
            await Task.Delay(TimeSpan.FromMinutes(10));
            try { if (Directory.Exists(view)) TouchAlive(view); } catch (IOException) { }
        }
    }

    /// <summary>The run is over: drop its view (the objects stay for the next run).</summary>
    private void RemoveView()
    {
        if (_view is null || !Directory.Exists(_view)) return;
        try { Directory.Delete(_view, recursive: true); } catch (IOException e) { log.WriteLine($"[mlp-shim] could not remove the dataset view: {e.Message}"); }
    }

    private static (long Files, long Bytes) Usage(string objects)
    {
        long n = 0, b = 0;
        foreach (var f in new DirectoryInfo(objects).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (f.Name.Contains(".part-", StringComparison.Ordinal))
            {
                // partial download of a run that was killed: swept once it is clearly abandoned
                if (TimeProvider.System.GetUtcNow().UtcDateTime - f.LastWriteTimeUtc > TimeSpan.FromHours(6)) try { f.Delete(); } catch (IOException) { }
                continue;
            }
            n++;
            b += f.Length;
        }
        return (n, b);
    }

    private static string Bytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{b / 1024.0:0} KB",
        _ => $"{b} B",
    };
}
