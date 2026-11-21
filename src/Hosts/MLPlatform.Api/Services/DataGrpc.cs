using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Contracts.V1;
using Empty = MLPlatform.Contracts.V1.Empty;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Services;

public sealed class DataGrpc(IMediator mediator, IHttpClientFactory http) : DataService.DataServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    public override async Task<Dataset> CreateDataset(CreateDatasetRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreateDatasetCommand(G(r.WorkspaceId, "workspace_id"), r.Slug, r.Name, r.Task), ctx.CancellationToken));

    public override async Task<ListDatasetsResponse> ListDatasets(ListDatasetsRequest r, ServerCallContext ctx)
    {
        var res = new ListDatasetsResponse();
        res.Datasets.AddRange((await mediator.Query(new ListDatasetsQuery(G(r.WorkspaceId, "workspace_id"), r.Task), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<Dataset> GetDataset(GetDatasetRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Query(new GetDatasetQuery(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id")), ctx.CancellationToken));

    public override async Task<UploadTarget> CreateUpload(CreateUploadRequest r, ServerCallContext ctx)
    {
        var (url, exp) = await mediator.Send(new CreateUploadCommand(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id"), r.Path, r.SizeBytes), ctx.CancellationToken);
        return new UploadTarget { Url = url, Method = "PUT", ExpiresAt = Timestamp.FromDateTimeOffset(exp) };
    }

    public override async Task<DatasetFile> CompleteUpload(CompleteUploadRequest r, ServerCallContext ctx)
    {
        var f = await mediator.Send(new CompleteUploadCommand(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id"), r.Path), ctx.CancellationToken);
        return new DatasetFile { Path = f.Path, SizeBytes = f.SizeBytes, UploadedAt = Timestamp.FromDateTimeOffset(f.UploadedAt) };
    }

    public override async Task<ListFilesResponse> ListFiles(GetDatasetRequest r, ServerCallContext ctx)
    {
        var res = new ListFilesResponse();
        res.Files.AddRange((await mediator.Query(new ListFilesQuery(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id")), ctx.CancellationToken))
            .Select(f => new DatasetFile { Path = f.Path, SizeBytes = f.SizeBytes, UploadedAt = Timestamp.FromDateTimeOffset(f.UploadedAt) }));
        return res;
    }

    public override async Task<DatasetVersion> CreateVersion(CreateVersionRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreateVersionCommand(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id"), r.Note), ctx.CancellationToken));

    public override async Task<ListVersionsResponse> ListVersions(GetDatasetRequest r, ServerCallContext ctx)
    {
        var res = new ListVersionsResponse();
        var ws = G(r.WorkspaceId, "workspace_id");
        var versions = await mediator.Query(new ListVersionsQuery(ws, G(r.DatasetId, "dataset_id")), ctx.CancellationToken);
        var splits = (await mediator.Query(new ListVersionSplitsQuery(ws, versions.Select(v => v.Id).ToArray()), ctx.CancellationToken)).ToDictionary(x => x.Id);
        foreach (var v in versions)
        {
            var p = ToProto(v);
            if (splits.GetValueOrDefault(v.Id) is { SplitState: { } state } split)
            {
                p.SplitState = state;
                foreach (var (k, n) in SplitRules.ParseCounts(split.SplitCounts)) p.SplitCounts[k] = n;
            }
            res.Versions.Add(p);
        }
        return res;
    }

    public override async Task<DatasetVersion> SplitVersion(SplitVersionRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        var v = await mediator.Send(new SplitVersionCommand(ws, G(r.VersionId, "version_id"), r.TrainPct, r.ValPct, r.TestPct, r.GroupBy, r.Note), ctx.CancellationToken);
        var info = await mediator.Send(new DescribeVersionSplitCommand(ws, v.Id), ctx.CancellationToken);
        var p = ToProto(v);
        p.SplitState = info.State;
        foreach (var (k, n) in info.Counts) p.SplitCounts[k] = n;
        return p;
    }

    private static Dataset ToProto(DatasetRow d) => new()
    {
        Id = d.Id.ToString(), WorkspaceId = d.WorkspaceId.ToString(), Slug = d.Slug, Name = d.Name, Task = d.Task, Status = d.Status,
        FileCount = d.FileCount, SizeBytes = d.SizeBytes, CurrentVersion = d.CurrentVersion, CreatedAt = Timestamp.FromDateTimeOffset(d.CreatedAt),
        PendingFiles = d.PendingFiles, PendingRemovals = d.PendingRemovals,
    };

    public override async Task<ChangeCount> RemoveFiles(DatasetPathsRequest r, ServerCallContext ctx) =>
        new() { Count = await mediator.Send(new RemoveDatasetFilesCommand(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id"), [.. r.Paths]), ctx.CancellationToken) };

    public override async Task<ChangeCount> KeepFiles(DatasetPathsRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new KeepDatasetFilesCommand(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id"), [.. r.Paths]), ctx.CancellationToken);
        return new ChangeCount { Count = r.Paths.Count };
    }

    public override async Task<PendingRemovals> ListPendingRemovals(GetDatasetRequest r, ServerCallContext ctx)
    {
        var res = new PendingRemovals();
        res.Paths.AddRange(await mediator.Query(new ListPendingRemovalsQuery(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id")), ctx.CancellationToken));
        return res;
    }

    public override async Task<Empty> DeleteVersion(DeleteVersionRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new DeleteVersionCommand(G(r.WorkspaceId, "workspace_id"), G(r.VersionId, "version_id")), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<Empty> DeleteDataset(GetDatasetRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new DeleteDatasetCommand(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id")), ctx.CancellationToken);
        return new Empty();
    }

    private static DatasetVersion ToProto(VersionRow v) => new()
    {
        Id = v.Id.ToString(), DatasetId = v.DatasetId.ToString(), Number = v.Number, FileCount = v.FileCount, SizeBytes = v.SizeBytes,
        Note = v.Note, CreatedAt = Timestamp.FromDateTimeOffset(v.CreatedAt), Storage = v.Storage, SourceRef = v.SourceRef ?? "",
    };

    // ───────────── Content browser ─────────────

    private static Guid? Opt(string s, string f) => string.IsNullOrEmpty(s) ? null : G(s, f);

    public override async Task<BrowseFilesResponse> BrowseFiles(BrowseFilesRequest r, ServerCallContext ctx)
    {
        Guid? version;
        if (r.VersionId == "latest")
            version = (await mediator.Query(new ListVersionsQuery(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id")), ctx.CancellationToken))
                .OrderByDescending(v => v.Number).Select(v => (Guid?)v.Id).FirstOrDefault();
        else version = Opt(r.VersionId, "version_id");
        var b = await mediator.Query(new BrowseDatasetQuery(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id"), version,
            r.Prefix, r.Search, r.Kind, r.Recursive, r.Offset, r.Limit <= 0 ? 60 : r.Limit), ctx.CancellationToken);
        var res = new BrowseFilesResponse { Total = b.Total, ClassesJson = b.ClassesJson };
        res.Folders.AddRange(b.Folders.Select(f => new BrowseFolder { Name = f.Name, Files = f.Files, SizeBytes = f.SizeBytes }));
        res.Files.AddRange(b.Files.Select(f => new BrowseFile
            { Path = f.Path, SizeBytes = f.SizeBytes, Kind = f.Kind, Url = f.Url, LabelUrl = f.LabelUrl ?? "", LabelPath = f.LabelPath ?? "", AnnotationsJson = f.AnnotationsJson ?? "" }));
        return res;
    }

    public override async Task<ContentStats> GetContentStats(ContentStatsRequest r, ServerCallContext ctx)
    {
        var s = await mediator.Query(new GetDatasetContentStatsQuery(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id"), Opt(r.VersionId, "version_id")), ctx.CancellationToken);
        var res = new ContentStats { Files = s.Files, SizeBytes = s.SizeBytes };
        res.Extensions.AddRange(s.Extensions.Select(e => new ExtensionStat { Ext = e.Ext, Kind = FileKinds.Of("x." + e.Ext), Files = e.Files, SizeBytes = e.SizeBytes }));
        return res;
    }

    public override async Task<ReadFileResponse> ReadFile(ReadFileRequest r, ServerCallContext ctx)
    {
        var (url, size) = await mediator.Query(new ResolveDatasetFileQuery(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id"),
            Opt(r.VersionId, "version_id"), r.Path), ctx.CancellationToken);
        var max = Math.Clamp(r.MaxBytes <= 0 ? 262_144 : r.MaxBytes, 1, 2 * 1024 * 1024);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, max - 1);
        try
        {
            using var res = await http.CreateClient("eval").SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.CancellationToken);
            if (!res.IsSuccessStatusCode) throw new UnavailableException("DATA_SOURCE_UNAVAILABLE", $"The file source answered HTTP {(int)res.StatusCode}");
            await using var body = await res.Content.ReadAsStreamAsync(ctx.CancellationToken);
            var buf = new byte[max];
            var n = 0;
            while (n < max)
            {
                var k = await body.ReadAsync(buf.AsMemory(n, max - n), ctx.CancellationToken);
                if (k == 0) break;
                n += k;
            }
            return new ReadFileResponse { Data = Google.Protobuf.ByteString.CopyFrom(buf, 0, n), Truncated = size > n, SizeBytes = size };
        }
        catch (HttpRequestException e) { throw new UnavailableException("DATA_SOURCE_UNAVAILABLE", "Could not read the file from its source: " + e.Message); }
    }
}
