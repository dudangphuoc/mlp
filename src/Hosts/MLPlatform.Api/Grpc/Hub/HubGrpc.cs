using System.Globalization;
using System.Text.Json.Nodes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Empty = MLPlatform.Contracts.V1.Empty;

namespace MLPlatform.Api.Grpc;

/// <summary>Hugging Face Hub integration: search, view repos, import datasets/models, workspace token.</summary>
public sealed class HubGrpc(IMediator mediator, HfHub hub, SecretBox secrets, HubImporter importer) : HubService.HubServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    private async Task<string?> TokenAsync(Guid ws, CancellationToken ct) => secrets.Open(await mediator.Query(new GetHubSecretQuery(ws), ct));

    private static HubRepoSummary Summary(JsonNode n, string kind)
    {
        var tags = (n["tags"] as JsonArray)?.Select(t => t?.ToString() ?? "").ToList() ?? [];
        var s = new HubRepoSummary
        {
            Id = n["id"]?.ToString() ?? n["modelId"]?.ToString() ?? "", Kind = kind,
            Task = n["pipeline_tag"]?.ToString() ?? tags.FirstOrDefault(t => t.StartsWith("task_categories:", StringComparison.Ordinal))?[16..] ?? "",
            Downloads = n["downloads"]?.GetValue<long>() ?? 0, Likes = n["likes"]?.GetValue<long>() ?? 0,
            License = (n["cardData"]?["license"] is JsonArray la ? string.Join(", ", la.Select(x => x?.ToString())) : n["cardData"]?["license"]?.ToString())
                ?? tags.FirstOrDefault(t => t.StartsWith("license:", StringComparison.Ordinal))?[8..] ?? "",
            Gated = n["gated"] is JsonValue gv && (gv.TryGetValue<bool>(out var gb) ? gb : gv.ToString() is "auto" or "manual"),
            Private = n["private"]?.GetValue<bool>() ?? false, Library = n["library_name"]?.ToString() ?? "",
        };
        if (DateTimeOffset.TryParse(n["lastModified"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var lm))
            s.LastModified = Timestamp.FromDateTimeOffset(lm);
        s.Tags.AddRange(tags.Where(t => !t.Contains(':')).Take(12));
        return s;
    }

    public override async Task<SearchHubResponse> SearchHub(SearchHubRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        await mediator.Query(new GetHubTokenStatusQuery(ws), ctx.CancellationToken);   // checks workspace read permission
        var kind = r.Kind == "dataset" ? "dataset" : "model";
        var res = new SearchHubResponse();
        var page = await hub.SearchAsync(kind, r.Query, r.Task, r.Sort, r.Limit > 0 ? r.Limit : 30, r.Cursor, await TokenAsync(ws, ctx.CancellationToken), ctx.CancellationToken);
        res.Items.AddRange(page.Items.Where(n => n is not null).Select(n => Summary(n!, kind)));
        res.NextCursor = page.Next ?? "";
        return res;
    }

    /// <summary>Fast repo view: info + top-level entries only (folders are opened with ListHubTree; the card loads separately).</summary>
    public override async Task<HubRepo> GetHubRepo(GetHubRepoRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        var status = await mediator.Query(new GetHubTokenStatusQuery(ws), ctx.CancellationToken);
        var kind = r.Kind == "dataset" ? "dataset" : "model";
        var rev = string.IsNullOrWhiteSpace(r.Revision) ? "main" : r.Revision.Trim();
        var token = await TokenAsync(ws, ctx.CancellationToken);
        var info = await hub.InfoAsync(kind, r.Repo.Trim(), rev, token, ctx.CancellationToken);
        var repo = new HubRepo { Summary = Summary(info, kind), Revision = rev, Sha = info["sha"]?.ToString() ?? "" };
        repo.NeedsToken = (repo.Summary.Gated || repo.Summary.Private) && status?.Status != "ok";
        try
        {
            var files = await hub.ListFolderAsync(kind, r.Repo.Trim(), rev, "", token, ctx.CancellationToken);
            repo.Files.AddRange(files.OrderBy(f => !f.Dir).ThenBy(f => f.Path, StringComparer.Ordinal).Select(ToFile));
        }
        catch (ForbiddenException) { repo.NeedsToken = true; }
        return repo;
    }

    public override async Task<ListHubTreeResponse> ListHubTree(ListHubTreeRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        await mediator.Query(new GetHubTokenStatusQuery(ws), ctx.CancellationToken);   // workspace read permission
        var kind = r.Kind == "dataset" ? "dataset" : "model";
        var rev = string.IsNullOrWhiteSpace(r.Revision) ? "main" : r.Revision.Trim();
        var files = await hub.ListFolderAsync(kind, r.Repo.Trim(), rev, r.Path ?? "", await TokenAsync(ws, ctx.CancellationToken), ctx.CancellationToken);
        var res = new ListHubTreeResponse();
        res.Files.AddRange(files.OrderBy(f => !f.Dir).ThenBy(f => f.Path, StringComparer.Ordinal).Select(ToFile));
        return res;
    }

    public override async Task<HubCard> GetHubCard(GetHubRepoRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        await mediator.Query(new GetHubTokenStatusQuery(ws), ctx.CancellationToken);
        var kind = r.Kind == "dataset" ? "dataset" : "model";
        var rev = string.IsNullOrWhiteSpace(r.Revision) ? "main" : r.Revision.Trim();
        return new HubCard { Excerpt = await hub.CardExcerptAsync(kind, r.Repo.Trim(), rev, await TokenAsync(ws, ctx.CancellationToken), ctx.CancellationToken) };
    }

    private static HubFile ToFile(HubFileInfo f) => new() { Path = f.Path, Size = f.Size, Lfs = f.Lfs, Dir = f.Dir };

    private static HubImport Map(HubImportRow i) => new()
    {
        Id = i.Id.ToString(), Kind = i.Kind, Repo = i.Repo, Revision = i.Revision, TargetId = i.TargetId?.ToString() ?? "", TargetName = i.TargetName,
        Task = i.Task, State = i.State, FilesTotal = i.FilesTotal, FilesDone = i.FilesDone, BytesTotal = i.BytesTotal, BytesDone = i.BytesDone,
        Message = i.Message, CreatedAt = Timestamp.FromDateTimeOffset(i.CreatedAt), FinishedAt = i.FinishedAt is { } f ? Timestamp.FromDateTimeOffset(f) : null,
        Source = i.Source, Mode = i.Mode,
    };

    public override async Task<HubImport> StartHubImport(StartHubImportRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        var source = string.IsNullOrEmpty(r.Source) ? "hf" : r.Source;
        var mode = string.IsNullOrEmpty(r.Mode) ? "copy" : r.Mode;
        var id = await mediator.Send(new StartHubImportCommand(ws, r.Kind, r.Repo, r.Revision, source == "url" ? [.. r.Urls] : [.. r.Include],
            string.IsNullOrEmpty(r.DatasetId) ? null : G(r.DatasetId, "dataset_id"), r.TargetName, r.Task, source, mode), ctx.CancellationToken);
        importer.Enqueue(id);
        return (await mediator.Query(new ListHubImportsQuery(ws), ctx.CancellationToken)).Where(i => i.Id == id).Select(Map).First();
    }

    public override async Task<ListHubImportsResponse> ListHubImports(ListHubImportsRequest r, ServerCallContext ctx)
    {
        var res = new ListHubImportsResponse();
        res.Imports.AddRange((await mediator.Query(new ListHubImportsQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(Map));
        return res;
    }

    private static HubTokenStatus Status(IntegrationRow? i) => i is null ? new HubTokenStatus() : new HubTokenStatus
    {
        Configured = true, Account = i.Account ?? "", Status = i.Status, CheckedAt = i.LastCheckedAt is { } c ? Timestamp.FromDateTimeOffset(c) : null,
    };

    public override async Task<HubTokenStatus> GetHubToken(GetHubTokenRequest r, ServerCallContext ctx) =>
        Status(await mediator.Query(new GetHubTokenStatusQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken));

    public override async Task<HubTokenStatus> SetHubToken(SetHubTokenRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        var token = r.Token.Trim();
        if (!token.StartsWith("hf_", StringComparison.Ordinal)) throw new ValidationException(new Dictionary<string, string> { ["token"] = "Hugging Face tokens start with hf_" });
        var account = await hub.WhoAmIAsync(token, ctx.CancellationToken);
        await mediator.Send(new SetHubTokenCommand(ws, secrets.Seal(token), account, account is not null), ctx.CancellationToken);
        return Status(await mediator.Query(new GetHubTokenStatusQuery(ws), ctx.CancellationToken));
    }

    public override async Task<Empty> DeleteHubToken(GetHubTokenRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new DeleteHubTokenCommand(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken);
        return new Empty();
    }
}
