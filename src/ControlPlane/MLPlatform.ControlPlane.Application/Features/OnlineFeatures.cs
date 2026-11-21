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

public static class OnlineFeatures
{
    public static async Task<IReadOnlyList<OnlineValue>> LookupAsync(IReadSession db, IClock clock, Guid ws, IReadOnlyList<string> keys, IEnumerable<string> refs,
        Guid? entityId, CancellationToken ct)
    {
        if (keys.Count is 0 or > 1000) throw new InvalidArgumentException("FEATURE_KEYS_INVALID", "Requires 1–1000 entities");
        var groups = await FeatureLookup.ResolveAsync(db, ws, refs, entityId, ct);
        var names = FeatureLookup.ColumnNames(groups);
        var result = keys.Distinct().ToDictionary(k => k, _ => new Acc());
        foreach (var (view, feats) in groups)
        {
            if (!view.Online) throw new PreconditionException("FEATURE_VIEW_NOT_ONLINE", $"View {view.Name} does not have online enabled");
            var found = (await db.OnlineLookupAsync(view.Id, [.. result.Keys], ct)).ToDictionary(r => r.EntityKey);
            foreach (var (k, acc) in result)
            {
                var fresh = found.TryGetValue(k, out var row) && clock.UtcNow - row.EventTs <= TimeSpan.FromHours(view.TtlHours);
                var vals = fresh ? JsonNode.Parse(row!.Values)!.AsObject() : null;
                foreach (var f in feats)
                {
                    var col = names[(view.Id, f)];
                    if (vals?[f] is { } v) acc.Values[col] = v.DeepClone(); else { acc.Values[col] = null; acc.Missing.Add(col); }
                }
                if (fresh && (acc.Oldest is null || row!.EventTs < acc.Oldest)) acc.Oldest = row!.EventTs;
            }
        }
        return keys.Select(k => new OnlineValue(k, result[k].Values, result[k].Missing, result[k].Oldest)).ToList();
    }

    private sealed class Acc
    {
        public Dictionary<string, JsonNode?> Values { get; } = [];
        public List<string> Missing { get; } = [];
        public DateTimeOffset? Oldest { get; set; }
    }
}
