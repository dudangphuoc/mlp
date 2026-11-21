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

internal static class FeatureLookup
{
    /// <summary>Groups references by view, checks that features exist and share the same entity.</summary>
    public static async Task<List<(ViewRow View, List<string> Features)>> ResolveAsync(IReadSession db, Guid ws, IEnumerable<string> refs, Guid? entityId, CancellationToken ct)
    {
        var groups = new List<(ViewRow, List<string>)>();
        foreach (var g in refs.Select(FeatureRef.Parse).GroupBy(r => (r.View, r.Version)))
        {
            var v = await db.ResolveViewAsync(ws, g.Key.View, g.Key.Version, ct)
                    ?? throw new NotFoundException("FEATURE_VIEW_NOT_FOUND", $"Feature view {g.Key.View}{(g.Key.Version > 0 ? "@" + g.Key.Version : "")} not found");
            if (v.State != "ready") throw new PreconditionException("FEATURE_VIEW_NOT_READY", $"View {v.Name}@{v.Version} is not materialized yet ({v.State}: {v.StateReason})");
            if (entityId is { } e && v.EntityId != e) throw new InvalidArgumentException("FEATURE_ENTITY_MISMATCH", $"View {v.Name} belongs to entity {v.EntityName}");
            entityId ??= v.EntityId;
            var defined = Defined(v);
            var missing = g.Select(r => r.Feature).Where(f => !defined.Contains(f)).ToList();
            if (missing.Count > 0) throw new NotFoundException("FEATURE_NOT_FOUND", $"View {v.Name}@{v.Version} is missing: {string.Join(", ", missing)}");
            groups.Add((v, g.Select(r => r.Feature).Distinct().ToList()));
        }
        if (groups.Count == 0) throw new InvalidArgumentException("FEATURE_REF_INVALID", "At least one feature is required");
        return groups;
    }

    public static HashSet<string> Defined(ViewRow v) => JsonNode.Parse(v.Features)!.AsArray().Select(f => f!["name"]!.ToString()).ToHashSet();

    /// <summary>Result column name: the feature name; duplicated across views → `view.feature`.</summary>
    public static Dictionary<(Guid, string), string> ColumnNames(List<(ViewRow View, List<string> Features)> groups)
    {
        var dup = groups.SelectMany(g => g.Features).GroupBy(f => f).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet();
        return groups.SelectMany(g => g.Features.Select(f => (Key: (g.View.Id, f), Name: dup.Contains(f) ? $"{g.View.Name}.{f}" : f))).ToDictionary(x => x.Key, x => x.Name);
    }
}
