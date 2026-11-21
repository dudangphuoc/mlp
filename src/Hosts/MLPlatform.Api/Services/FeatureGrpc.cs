using MLPlatform.Api.Infrastructure;
using MLPlatform.ControlPlane.Infrastructure;
using System.Text.Json.Nodes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Features;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Services;

public sealed class FeatureGrpc(IMediator mediator) : FeatureService.FeatureServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });
    private static Timestamp? Ts(DateTimeOffset? t) => t is { } v ? Timestamp.FromDateTimeOffset(v) : null;

    public override async Task<FeatureEntity> CreateEntity(CreateEntityRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreateEntityCommand(G(r.WorkspaceId, "workspace_id"), r.Name, r.JoinKey, r.Description), ctx.CancellationToken));

    public override async Task<ListEntitiesResponse> ListEntities(ListEntitiesRequest r, ServerCallContext ctx)
    {
        var res = new ListEntitiesResponse();
        res.Entities.AddRange((await mediator.Query(new ListEntitiesQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<FeatureView> CreateView(CreateFeatureViewRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreateFeatureViewCommand(G(r.WorkspaceId, "workspace_id"), r.Name, r.Entity, G(r.SourceVersionId, "source_version_id"),
            r.TimestampColumn, r.Features.ToList(), r.TtlHours, r.Online, r.MaterializeEveryMin > 0 ? r.MaterializeEveryMin : null), ctx.CancellationToken));

    public override async Task<ListFeatureViewsResponse> ListViews(ListFeatureViewsRequest r, ServerCallContext ctx)
    {
        var res = new ListFeatureViewsResponse();
        res.Views.AddRange((await mediator.Query(new ListFeatureViewsQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<FeatureView> Materialize(FeatureViewRef r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new MaterializeViewCommand(G(r.WorkspaceId, "workspace_id"), G(r.ViewId, "view_id")), ctx.CancellationToken));

    public override async Task<FeatureTableResponse> GetHistoricalFeatures(GetHistoricalFeaturesRequest r, ServerCallContext ctx)
    {
        var t = await mediator.Query(new GetHistoricalFeaturesQuery(G(r.WorkspaceId, "workspace_id"),
            r.Spine.Select(s => (s.EntityKey, s.At?.ToDateTimeOffset() ?? DateTimeOffset.MaxValue)).ToList(), r.Features.ToList()), ctx.CancellationToken);
        var res = new FeatureTableResponse();
        res.Columns.AddRange(t.Columns);
        foreach (var row in t.Rows) res.Rows.Add(ToList(row));
        return res;
    }

    public override async Task<FeatureTableResponse> GetOnlineFeatures(GetOnlineFeaturesRequest r, ServerCallContext ctx)
    {
        var vals = await mediator.Query(new GetOnlineFeaturesQuery(G(r.WorkspaceId, "workspace_id"), r.EntityKeys.ToList(), r.Features.ToList()), ctx.CancellationToken);
        var res = new FeatureTableResponse();
        res.Columns.Add("entity");
        if (vals.Count > 0) res.Columns.AddRange(vals[0].Values.Keys);
        foreach (var v in vals)
        {
            res.Rows.Add(ToList(new JsonNode?[] { JsonValue.Create(v.Key) }.Concat(v.Values.Values).ToList()));
            res.Missing.AddRange(v.Missing.Select(m => $"{v.Key}/{m}"));
        }
        return res;
    }

    public override async Task<FeatureTableResponse> SampleOnline(SampleOnlineRequest r, ServerCallContext ctx)
    {
        var rows = await mediator.Query(new OnlineSampleQuery(G(r.WorkspaceId, "workspace_id"), G(r.ViewId, "view_id"), r.Limit), ctx.CancellationToken);
        var res = new FeatureTableResponse();
        var cols = rows.SelectMany(x => JsonNode.Parse(x.Values)!.AsObject().Select(kv => kv.Key)).Distinct().ToList();
        res.Columns.Add("entity"); res.Columns.Add("event_time"); res.Columns.AddRange(cols);
        foreach (var x in rows)
        {
            var o = JsonNode.Parse(x.Values)!.AsObject();
            res.Rows.Add(ToList(new List<JsonNode?> { JsonValue.Create(x.EntityKey), JsonValue.Create(x.EventTs.UtcDateTime.ToString("O")) }.Concat(cols.Select(c => o[c])).ToList()));
        }
        return res;
    }

    public override async Task<DeploymentFeatures> BindDeployment(BindDeploymentRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new BindDeploymentFeaturesCommand(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id"), r.Entity, r.Features.ToList()), ctx.CancellationToken));

    public override async Task<DeploymentFeatures> GetDeploymentFeatures(DeploymentFeaturesRef r, ServerCallContext ctx) =>
        ToProto(await mediator.Query(new GetDeploymentFeaturesQuery(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id")), ctx.CancellationToken));

    private static ListValue ToList(IEnumerable<JsonNode?> xs)
    {
        var l = new ListValue();
        foreach (var x in xs)
            l.Values.Add(x switch
            {
                null => Value.ForNull(),
                JsonValue v when v.TryGetValue<double>(out var d) => Value.ForNumber(d),
                JsonValue v when v.TryGetValue<bool>(out var b) => Value.ForBool(b),
                _ => Value.ForString(x is JsonValue sv && sv.TryGetValue<string>(out var s) ? s : x.ToJsonString()),
            });
        return l;
    }

    private static FeatureEntity ToProto(EntityRow e) => new() { Id = e.Id.ToString(), Name = e.Name, JoinKey = e.JoinKey, Description = e.Description, CreatedAt = Ts(e.CreatedAt) };

    private static FeatureView ToProto(ViewRow v)
    {
        var p = new FeatureView
        {
            Id = v.Id.ToString(), Name = v.Name, Version = v.Version, Entity = v.EntityName, JoinKey = v.JoinKey, SourceVersionId = v.SourceVersionId.ToString(),
            TimestampColumn = v.TimestampColumn, TtlHours = v.TtlHours, Online = v.Online, MaterializeEveryMin = v.MaterializeEveryMin ?? 0, State = v.State,
            StateReason = v.StateReason ?? "", Rows = v.Rows, Entities = v.Entities, LastMaterializedAt = Ts(v.LastMaterializedAt), MaxEventTs = Ts(v.MaxEventTs), CreatedAt = Ts(v.CreatedAt),
        };
        p.Features.AddRange(JsonNode.Parse(v.Features)!.AsArray().Select(f => new FeatureDef { Name = f!["name"]!.ToString(), Type = f["type"]!.ToString() }));
        return p;
    }

    private static DeploymentFeatures ToProto(BindingRow? b)
    {
        if (b is null) return new DeploymentFeatures { Bound = false };
        var p = new DeploymentFeatures { Bound = true, Entity = b.EntityName, JoinKey = b.JoinKey, UpdatedAt = Ts(b.UpdatedAt) };
        p.Features.AddRange(JsonNode.Parse(b.Features)!.AsArray().Select(x => x!.ToString()));
        return p;
    }
}
