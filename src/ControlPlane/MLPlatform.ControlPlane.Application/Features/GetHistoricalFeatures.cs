using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Features;

/// <summary>Training data: point-in-time join (≤ 10,000 spine rows) → table [entity, ts, feature…] (no leakage of values after ts).</summary>
public sealed record GetHistoricalFeaturesQuery(Guid WorkspaceId, IReadOnlyList<(string Key, DateTimeOffset Ts)> Spine, IReadOnlyList<string> Refs) : IQuery<FeatureTable>;

public sealed class GetHistoricalFeaturesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetHistoricalFeaturesQuery, FeatureTable>
{
    public async ValueTask<FeatureTable> Handle(GetHistoricalFeaturesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        if (q.Spine.Count is 0 or > 10_000) throw new InvalidArgumentException("FEATURE_SPINE_INVALID", "Requires 1–10,000 rows (entity, timestamp)");
        var groups = await FeatureLookup.ResolveAsync(db, q.WorkspaceId, q.Refs, null, ct);
        var names = FeatureLookup.ColumnNames(groups);
        var rows = q.Spine.Select(s => new List<JsonNode?> { JsonValue.Create(s.Key), JsonValue.Create(s.Ts.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)) }).ToList();
        var keys = q.Spine.Select(s => s.Key).ToArray();
        var times = q.Spine.Select(s => s.Ts.ToUniversalTime()).ToArray();
        foreach (var (view, feats) in groups)
        {
            var pit = await db.PointInTimeAsync(keys, times, view.Id, view.TtlHours, ct);
            foreach (var p in pit)
            {
                var vals = p.Values is null ? null : JsonNode.Parse(p.Values)!.AsObject();
                foreach (var f in feats) rows[(int)p.Idx - 1].Add(vals?[f]?.DeepClone());
            }
        }
        var cols = new List<string> { groups[0].View.JoinKey, "event_time" };
        cols.AddRange(groups.SelectMany(g => g.Features.Select(f => names[(g.View.Id, f)])));
        return new FeatureTable(cols, rows);
    }
}
