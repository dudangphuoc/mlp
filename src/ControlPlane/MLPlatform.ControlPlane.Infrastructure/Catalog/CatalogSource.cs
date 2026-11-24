using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Where the builtin recipes come from: a git repository at a branch, unpacked per commit;
/// Catalog:RecipesDir overrides it with a local folder for recipe authors.</summary>
public sealed class CatalogSource(IHttpClientFactory http, IConfiguration cfg, TimeProvider time, ILogger<CatalogSource> log)
{
    public const string DefaultSource = "https://github.com/dudangphuoc/mlp";
    private const long MaxArchiveBytes = 200L << 20;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Folder holding the recipes of the last sync; null before the first successful sync.</summary>
    public string? Directory { get; private set; }

    public string Origin => Local ?? cfg["Catalog:Source"] ?? DefaultSource;
    public string Ref => Local is null ? cfg["Catalog:Ref"] ?? "main" : "";

    /// <summary>The local recipe folder when Catalog:RecipesDir names one that exists; otherwise git is used.</summary>
    private string? Local => cfg["Catalog:RecipesDir"] is { Length: > 0 } d && System.IO.Directory.Exists(d) ? d : null;
    public string Commit { get; private set; } = "";
    public DateTimeOffset? SyncedAt { get; private set; }
    public string Error { get; private set; } = "";
    public int Recipes { get; private set; }

    /// <summary>Fetches the branch's current commit (or reads the local folder) and syncs the builtin recipes; returns
    /// how many recipes were read.</summary>
    public async Task<int> SyncAsync(IMediator mediator, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var dir = Local ?? await FetchAsync(ct);
            var count = await mediator.Send(new SyncBuiltinRecipesCommand(dir), ct);
            (Directory, SyncedAt, Error, Recipes) = (dir, time.GetUtcNow(), "", count);
            log.LogInformation("Catalog: synced {Count} builtin recipe(s) from {Origin} {Ref} {Commit}", count, Origin, Ref, Commit);
            return count;
        }
        catch (Exception e) when (e is AppException or HttpRequestException or IOException or InvalidDataException)
        {
            Error = e.Message;
            throw;
        }
        finally { _gate.Release(); }
    }

    private async Task<string> FetchAsync(CancellationToken ct)
    {
        var commit = await ResolveAsync(ct);
        var root = Path.Combine(cfg["Catalog:CacheDir"] ?? Path.Combine(Path.GetTempPath(), "mlp-catalog"), commit);
        if (!System.IO.Directory.Exists(root)) await UnpackAsync(commit, root, ct);
        Commit = commit;
        return root;
    }

    private async Task<string> ResolveAsync(CancellationToken ct)
    {
        using var request = GitArchives.CommitRequest(Origin, Ref, cfg["Catalog:GitHubToken"]);
        using var response = await http.CreateClient("code-sources").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new UnavailableException("CATALOG_SOURCE_UNAVAILABLE", $"{Origin} answered {(int)response.StatusCode} for branch {Ref}");
        return GitArchives.ParseCommit(await response.Content.ReadAsStringAsync(ct))
               ?? throw new PreconditionException("CATALOG_REF_NOT_FOUND", $"'{Ref}' did not resolve to a commit in {Origin}");
    }

    private async Task UnpackAsync(string commit, string root, CancellationToken ct)
    {
        using var request = GitArchives.Request(Origin, commit, cfg["Catalog:GitHubToken"]);
        using var response = await http.CreateClient("code-sources").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new UnavailableException("CATALOG_SOURCE_UNAVAILABLE", $"{Origin} answered {(int)response.StatusCode} for the archive of {commit[..12]}");
        if (response.Content.Headers.ContentLength > MaxArchiveBytes) throw new PreconditionException("CATALOG_SOURCE_TOO_LARGE", "Catalog archive is larger than 200 MB");
        var prefix = (cfg["Catalog:Path"] ?? "recipes").Trim('/') + "/";
        var temp = root + ".part-" + Guid.NewGuid().ToString("N")[..8];
        foreach (var (name, (data, _)) in GitArchives.Read(await response.Content.ReadAsByteArrayAsync(ct), stripTopFolder: true))
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var path = Path.Combine(temp, name[prefix.Length..]);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, data, ct);
        }
        if (!System.IO.Directory.Exists(temp)) throw new PreconditionException("CATALOG_PATH_EMPTY", $"{Origin}@{commit[..12]} has no {prefix} folder");
        System.IO.Directory.Move(temp, root);
    }
}
