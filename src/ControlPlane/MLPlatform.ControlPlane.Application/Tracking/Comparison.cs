using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A project working with a Catalog recipe ("used in" tab of the recipe page).</summary>
public sealed record RecipeUsageRow(Guid ProjectId, string ProjectName, Guid ProjectRecipeId, DateTimeOffset CreatedAt, int? LatestRevision, long RunCount,
    DateTimeOffset? LastRunAt);

/// <summary>One line that differs between the compared runs (or revisions): field, canvas pane, one shown value per item.</summary>
public sealed record CompareRow(string Field, string Pane, IReadOnlyList<string> Values);

/// <summary>
/// Round 1 §8.6: can the numbers be compared (same validation set, same metric axis), and only the lines that differ.
/// <paramref name="Axes"/> per run: "step" (epoch metrics moved onto the step axis, R25) or "epoch" (runs from before).
/// </summary>
public sealed record ComparisonView(bool SameVal, bool SameAxis, IReadOnlyList<string> Axes, IReadOnlyList<CompareRow> Rows);

/// <summary>Revision diff (round 1 §6 DiffRevisions): changed lines grouped by pane, and whether the recipe itself changed in between.</summary>
public sealed record RevisionDiffView(int From, int To, bool ManifestChanged, IReadOnlyList<DraftDiff> Rows);

/// <summary>Reads used by comparison and diff: the recipe copy's parameter schema, a value as people see it.</summary>
public static partial class ComparisonSql
{
    [SqlQuery("Track/RecipeUsage.sql")]
    public static partial Task<List<RecipeUsageRow>> RecipeUsageAsync(this IReadSession db, Guid workspaceId, string recipe, CancellationToken ct);
}

/// <summary>Compare 2–8 runs of one project.</summary>
public sealed record CompareRunsQuery(Guid WorkspaceId, IReadOnlyList<Guid> RunIds) : IQuery<ComparisonView>;

/// <summary>
/// Same val = every run trained on the same dataset version (its split is fixed, R8). Axis = where the primary metric's last point sits against
/// the epochs the run asked for: beyond them it is on the step axis. Rows: recipe, variant, data, start, kind, revision and every parameter whose
/// effective value (set, else the recipe default) differs.
/// </summary>
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

/// <summary>What changed between two revisions of a project recipe.</summary>
public sealed record DiffRevisionsQuery(Guid WorkspaceId, Guid ProjectRecipeId, int From, int To) : IQuery<RevisionDiffView>;

/// <summary>Same rows as the canvas's change list (variant, parameters by pane); a changed manifest is flagged, not diffed as text.</summary>
public sealed class DiffRevisionsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<DiffRevisionsQuery, RevisionDiffView>
{
    public async ValueTask<RevisionDiffView> Handle(DiffRevisionsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var a = await db.GetRevisionByNumberAsync(q.ProjectRecipeId, q.WorkspaceId, q.From, ct) ?? throw new NotFoundException("TRACK_REVISION_NOT_FOUND", $"Revision r{q.From} not found");
        var b = await db.GetRevisionByNumberAsync(q.ProjectRecipeId, q.WorkspaceId, q.To, ct) ?? throw new NotFoundException("TRACK_REVISION_NOT_FOUND", $"Revision r{q.To} not found");
        var schema = RecipeDrafts.Schema(JsonNode.Parse(b.Manifest)!.AsObject());
        return new RevisionDiffView(a.Number, b.Number, a.ManifestSha256 != b.ManifestSha256,
            RecipeDrafts.Diff(a.Variant, RecipeDrafts.Parse(a.Params), b.Variant, RecipeDrafts.Parse(b.Params), schema));
    }
}

/// <summary>Projects using a Catalog recipe.</summary>
public sealed record ListRecipeUsageQuery(Guid WorkspaceId, string Recipe) : IQuery<IReadOnlyList<RecipeUsageRow>>;

/// <summary>One query over the workspace's project copies of the recipe.</summary>
public sealed class ListRecipeUsageQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListRecipeUsageQuery, IReadOnlyList<RecipeUsageRow>>
{
    public async ValueTask<IReadOnlyList<RecipeUsageRow>> Handle(ListRecipeUsageQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.RecipeUsageAsync(q.WorkspaceId, q.Recipe, ct);
    }
}
