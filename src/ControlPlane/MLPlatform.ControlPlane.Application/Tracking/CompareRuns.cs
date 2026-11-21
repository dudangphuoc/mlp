using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Compare 2–8 runs of one project.</summary>
public sealed record CompareRunsQuery(Guid WorkspaceId, IReadOnlyList<Guid> RunIds) : IQuery<ComparisonView>;

/// <summary>Same val = every run trained on the same dataset version (its split is fixed, R8).</summary>
public sealed class CompareRunsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<CompareRunsQuery, ComparisonView>
{
    public async ValueTask<ComparisonView> Handle(CompareRunsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        if (q.RunIds.Count is < 2 or > 8) throw new InvalidArgumentException("TRACK_COMPARE_INVALID", "Pick 2 to 8 runs to compare");
        var runs = new List<RunRow>();
        foreach (var id in q.RunIds.Distinct())
            runs.Add(await db.GetRunAsync(id, ct) is { } r && r.WorkspaceId == q.WorkspaceId ? r : throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found"));
        var extras = (await db.RunExtrasAsync(q.WorkspaceId, [.. runs.Select(r => r.Id)], ct)).ToDictionary(x => x.RunId);

        var items = new List<(RunRow Run, string Variant, JsonObject Params, JsonObject Schema, string Revision, string Axis)>();
        foreach (var r in runs)
        {
            var x = extras.GetValueOrDefault(r.Id);
            var rev = x?.RevisionId is { } rid ? await db.GetRevisionAsync(rid, q.WorkspaceId, ct) : null;
            JsonObject manifest = rev is not null ? JsonNode.Parse(rev.Manifest)!.AsObject()
                : await db.GetProjectRecipeByNameAsync(r.ProjectId, q.WorkspaceId, r.Recipe, ct) is { } pr ? JsonNode.Parse(pr.OriginManifest)!.AsObject() : [];
            var schema = RecipeDrafts.Schema(manifest);
            var values = rev is not null ? RecipeDrafts.Parse(rev.Params) : RecipeDrafts.Normalise(RecipeDrafts.Parse(r.Params), schema);
            var shown = new JsonObject();
            foreach (var (k, def) in schema)
                if (def?["from"] is null && RecipeDrafts.Value(values, schema, k) is { } v) shown[k] = v.DeepClone();
            var epochs = RecipeDrafts.IntValue(values, schema, "epochs", 0);
            var primary = (await db.MetricSummaryAsync(r.Id, ct)).FirstOrDefault(m => m.Key == r.PrimaryMetricKey);
            var axis = primary is null ? "" : epochs > 0 && primary.LastStep <= epochs ? "epoch" : "step";
            items.Add((r, rev?.Variant ?? r.Variant, shown, schema, rev is null ? "" : $"r{x!.RevisionNumber}", axis));
        }

        var rows = new List<CompareRow>();
        void Line(string field, string pane, Func<int, string> value)
        {
            var vals = items.Select((_, i) => value(i)).ToList();
            if (vals.Distinct().Count() > 1) rows.Add(new CompareRow(field, pane, vals));
        }
        var versions = new Dictionary<Guid, string>();
        foreach (var v in runs.Select(r => r.DatasetVersionId).OfType<Guid>().Distinct())
            if (await db.GetVersionAsync(v, q.WorkspaceId, ct) is { } vr)
                versions[v] = $"{(await db.GetDatasetAsync(vr.DatasetId, q.WorkspaceId, ct))?.Name ?? "?"} v{vr.Number}";
        Line("recipe", "model", i => items[i].Run.Recipe);
        Line("variant", "model", i => items[i].Variant);
        Line("revision", "model", i => items[i].Revision);
        Line("data", "data", i => items[i].Run.DatasetVersionId is { } v ? versions.GetValueOrDefault(v, "?") : "");
        Line("start", "model", i => extras.GetValueOrDefault(items[i].Run.Id)?.StartKind ?? (items[i].Run.FromScratch ? "backbone" : "pretrained"));
        Line("train_kind", "model", i => extras.GetValueOrDefault(items[i].Run.Id) is { TrainKind: "retrain", RetrainOfNumber: { } n } ? $"retrain #{n}" : "fine_tune");
        foreach (var k in items.SelectMany(x => x.Params.Select(p => p.Key)).Distinct().Order(StringComparer.Ordinal))
            Line("params." + k, RecipeDrafts.Pane(items.First(x => x.Schema.ContainsKey(k)).Schema, k), i => Show(items[i].Params[k]));
        Line("gpus", "machine", i => items[i].Run.Gpus.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var axes = items.Select(x => x.Axis).ToList();
        var known = axes.Where(a => a.Length > 0).Distinct().Count();
        return new ComparisonView(runs.Select(r => r.DatasetVersionId).Distinct().Count() == 1 && runs[0].DatasetVersionId is not null, known <= 1, axes, rows);
    }

    private static string Show(JsonNode? v) => v is null ? "" : v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : v.ToJsonString();
}
