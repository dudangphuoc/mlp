using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Buckets per 20/05 §3. `mlp-datasets/{ws}/datasets/{ds}/raw/…` (live) and `…/v{n}/…` (immutable).</summary>
public static class Buckets
{
    /// <summary>Bucket names — configurable via MLP__S3__DatasetsBucket / ArtifactsBucket (set by the api at startup).</summary>
    public static string Datasets { get; set; } = "mlp-datasets";
    public static string Artifacts { get; set; } = "mlp-artifacts";
    public static string Raw(Guid ws, Guid ds) => $"{ws}/datasets/{ds}/raw/";
    public static string Version(Guid ws, Guid ds, int n) => $"{ws}/datasets/{ds}/v{n}/";

    /// <summary>Safe relative path: not empty, not absolute, no "..", no control characters.</summary>
    public static string SafePath(string p)
    {
        var x = p.Replace('\\', '/').Trim('/');
        if (x.Length == 0 || x.Length > 1024 || x.Split('/').Any(s => s is "" or "." or "..") || x.Any(char.IsControl))
            throw new ValidationException(new Dictionary<string, string> { ["path"] = "Invalid file path" });
        return x;
    }
}

public sealed record CreateDatasetCommand(Guid WorkspaceId, string Slug, string Name, string Task) : ICommand<DatasetRow>;

public sealed class CreateDatasetCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreateDatasetCommand, DatasetRow>
{
    public async ValueTask<DatasetRow> Handle(CreateDatasetCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        if (!IdentityRules.Slug().IsMatch(c.Slug))
            throw new ValidationException(new Dictionary<string, string> { ["slug"] = "Invalid slug" });
        var id = Ids.New();
        try { await db.InsertDatasetAsync(id, c.WorkspaceId, c.Slug, c.Name, c.Task, uid, clock.UtcNow, ct); }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("DATA_SLUG_TAKEN", "URL is already in use"); }
        return (await db.GetDatasetAsync(id, c.WorkspaceId, ct))!;
    }
}

public sealed record ListDatasetsQuery(Guid WorkspaceId, string Task) : IQuery<IReadOnlyList<DatasetRow>>;

public sealed class ListDatasetsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListDatasetsQuery, IReadOnlyList<DatasetRow>>
{
    public async ValueTask<IReadOnlyList<DatasetRow>> Handle(ListDatasetsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListDatasetsAsync(q.WorkspaceId, q.Task, ct);
    }
}

public sealed record GetDatasetQuery(Guid WorkspaceId, Guid DatasetId) : IQuery<DatasetRow>;

public sealed class GetDatasetQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetDatasetQuery, DatasetRow>
{
    public async ValueTask<DatasetRow> Handle(GetDatasetQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
    }
}

/// <summary>Browser/SDK uploads directly to S3 storage (the api only issues URLs — 20/01 §3). P1: single PUT ≤ 5 GiB; multipart later.</summary>
public sealed record CreateUploadCommand(Guid WorkspaceId, Guid DatasetId, string Path, long SizeBytes) : ICommand<(string Url, DateTimeOffset ExpiresAt)>;

public sealed class CreateUploadCommandHandler(IReadSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateUploadCommand, (string Url, DateTimeOffset ExpiresAt)>
{
    public async ValueTask<(string Url, DateTimeOffset ExpiresAt)> Handle(CreateUploadCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        if (c.SizeBytes > 5L * 1024 * 1024 * 1024) throw new ResourceExhaustedException("DATA_TOO_LARGE", "Files > 5 GiB require multipart upload (not supported yet)");
        var key = Buckets.Raw(c.WorkspaceId, c.DatasetId) + Buckets.SafePath(c.Path);
        var ttl = TimeSpan.FromHours(1);
        return (s3.Presign(HttpMethod.Put, Buckets.Datasets, key, ttl), clock.UtcNow + ttl);
    }
}

public sealed record CompleteUploadCommand(Guid WorkspaceId, Guid DatasetId, string Path) : ICommand<FileRow>;

public sealed class CompleteUploadCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<CompleteUploadCommand, FileRow>
{
    public async ValueTask<FileRow> Handle(CompleteUploadCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var path = Buckets.SafePath(c.Path);
        var head = await s3.HeadAsync(Buckets.Datasets, Buckets.Raw(c.WorkspaceId, c.DatasetId) + path, ct)
                   ?? throw new PreconditionException("DATA_UPLOAD_MISSING", "File not found in storage — upload has not finished");
        var now = clock.UtcNow;
        await db.UpsertFileAsync(c.DatasetId, path, head.Size, now, ct);
        return new FileRow(path, head.Size, now);
    }
}

public sealed record ListFilesQuery(Guid WorkspaceId, Guid DatasetId) : IQuery<IReadOnlyList<FileRow>>;

public sealed class ListFilesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListFilesQuery, IReadOnlyList<FileRow>>
{
    public async ValueTask<IReadOnlyList<FileRow>> Handle(ListFilesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        return await db.ListFilesAsync(q.DatasetId, ct);
    }
}

/// <summary>
/// Commit a version (M04 R5, R6): the latest version, minus the paths marked for removal, plus the files added since (the working copy).
/// Added files are copied to the version's own `v{n}/` folder; files that stay point at where they already are (no copy). The result is
/// immutable, and the working copy is empty again — it always holds only what changes in the next version.
/// </summary>
public sealed record CreateVersionCommand(Guid WorkspaceId, Guid DatasetId, string Note) : ICommand<VersionRow>;

public sealed class CreateVersionCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateVersionCommand, VersionRow>
{
    public async ValueTask<VersionRow> Handle(CreateVersionCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var files = await db.ListFilesAsync(c.DatasetId, ct);
        var removed = (await db.ListPendingRemovalsAsync(c.DatasetId, ct)).Select(r => r.Path).ToList();
        var baseVersion = await db.LatestVersionAsync(c.DatasetId, c.WorkspaceId, ct);
        if (files.Count == 0 && (removed.Count == 0 || baseVersion is null))
            throw new PreconditionException("DATA_NOTHING_TO_FREEZE", baseVersion is null ? "Dataset has no files yet" : "No changes since the latest version");
        var n = await db.NextVersionNumberAsync(c.DatasetId, c.WorkspaceId, ct);
        var prefix = Buckets.Version(c.WorkspaceId, c.DatasetId, n);
        foreach (var f in files)
            await s3.CopyAsync(Buckets.Datasets, Buckets.Raw(c.WorkspaceId, c.DatasetId) + f.Path, Buckets.Datasets, prefix + f.Path, ct);
        var id = Ids.New();
        var now = clock.UtcNow;
        await db.InsertVersionAsync(id, c.DatasetId, n, prefix, 0, 0, c.Note, uid, now, ct);
        foreach (var f in files) await db.InsertVersionFileAsync(id, f.Path, f.SizeBytes, ct);
        if (baseVersion is not null)
        {
            // The split file belongs to the old file set: the new version is split again (an image keeps its split — it depends on its path only).
            string[] skip = [.. files.Select(f => f.Path), .. removed, SplitRules.File];
            await db.InsertVersionFilesFromBaseAsync(id, baseVersion.Id, $"s3://{Buckets.Datasets}/{baseVersion.Prefix}", skip, ct);
        }
        await db.SetVersionTotalsAsync(id, ct);
        await db.ClearPendingChangesAsync(c.DatasetId, ct);
        return (await db.GetVersionAsync(id, c.WorkspaceId, ct))!;
    }
}

public sealed record ListVersionsQuery(Guid WorkspaceId, Guid DatasetId) : IQuery<IReadOnlyList<VersionRow>>;

public sealed class ListVersionsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListVersionsQuery, IReadOnlyList<VersionRow>>
{
    public async ValueTask<IReadOnlyList<VersionRow>> Handle(ListVersionsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        return await db.ListVersionsAsync(q.DatasetId, ct);
    }
}

/// <summary>For M09: the version's file list with presigned GETs (24 hours) so the shim can download into `{data}` (20/08 §5.1 step 2).</summary>
public sealed record ResolveVersionFilesQuery(Guid WorkspaceId, Guid VersionId) : IQuery<(VersionRow Version, string Task, IReadOnlyList<VersionFileLink> Files)>;

public sealed class ResolveVersionFilesQueryHandler(IReadSession db, S3Client s3, WorkspaceAuthorizer auth)
    : IQueryHandler<ResolveVersionFilesQuery, (VersionRow Version, string Task, IReadOnlyList<VersionFileLink> Files)>
{
    public async ValueTask<(VersionRow Version, string Task, IReadOnlyList<VersionFileLink> Files)> Handle(ResolveVersionFilesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        var v = await db.GetVersionAsync(q.VersionId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var ds = await db.GetDatasetAsync(v.DatasetId, q.WorkspaceId, ct);
        return (v, ds!.Task, await VersionFileUrls.SignAsync(db, s3, v, ct));
    }
}

/// <summary>One file of a version for a job: signed link, size, and the cache id (null = the bytes behind the address may change → never cached).</summary>
public sealed record VersionFileLink(string Path, string Url, long Size, string? Id)
{
    public System.Text.Json.Nodes.JsonObject ToJson()
    {
        var o = new System.Text.Json.Nodes.JsonObject { ["path"] = Path, ["url"] = Url, ["size"] = Size };
        if (Id is not null) o["id"] = Id;
        return o;
    }
}

/// <summary>Presigned GETs (24 hours) for a version's files: stored files from the dataset bucket, reference files from their source.</summary>
public static class VersionFileUrls
{
    public static async Task<IReadOnlyList<VersionFileLink>> SignAsync(IReadSession db, S3Client s3, VersionRow v, CancellationToken ct) =>
        (await db.ListVersionFilesAsync(v.Id, ct)).Select(f => new VersionFileLink(f.Path, f.Url is { } u ? SourceUrls.Resolve(s3, u)
            : s3.Presign(HttpMethod.Get, Buckets.Datasets, v.Prefix + f.Path, TimeSpan.FromHours(24), Audience.Workload),
            f.SizeBytes, DataCacheRules.Id(v, f.Path, f.Url, f.SizeBytes))).ToList();
}

/// <summary>
/// Reference version: records the files' source addresses without copying anything; the machine downloads them from the source
/// when a job uses the version. Immutability comes from the source being pinned (HF commit sha, fixed URLs/keys).
/// </summary>
public sealed record CreateReferenceVersionCommand(Guid WorkspaceId, Guid DatasetId, string Note, string SourceRef,
    IReadOnlyList<(string Path, long Size, string Url)> Files) : ICommand<VersionRow>;

public sealed class CreateReferenceVersionCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateReferenceVersionCommand, VersionRow>
{
    public async ValueTask<VersionRow> Handle(CreateReferenceVersionCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        if (c.Files.Count == 0) throw new PreconditionException("DATA_NOT_READY", "No files to reference");
        var n = await db.NextVersionNumberAsync(c.DatasetId, c.WorkspaceId, ct);
        var id = Ids.New();
        var now = clock.UtcNow;
        var size = c.Files.Sum(f => f.Size);
        await db.InsertReferenceVersionAsync(id, c.DatasetId, n, c.Files.Count, size, c.Note, uid, now, c.SourceRef, ct);
        foreach (var f in c.Files) await db.InsertVersionFileUrlAsync(id, Buckets.SafePath(f.Path), f.Size, f.Url, ct);
        return new VersionRow(id, c.DatasetId, n, "", c.Files.Count, size, c.Note, now, "reference", c.SourceRef);
    }
}
