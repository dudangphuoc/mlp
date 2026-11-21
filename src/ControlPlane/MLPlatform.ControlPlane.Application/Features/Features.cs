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

public sealed record EntityRow(Guid Id, string Name, string JoinKey, string Description, DateTimeOffset CreatedAt);
public sealed record ViewRow(Guid Id, Guid WorkspaceId, string Name, int Version, Guid EntityId, string EntityName, string JoinKey, Guid SourceVersionId,
    string TimestampColumn, string Features, int TtlHours, bool Online, int? MaterializeEveryMin, string State, string? StateReason, long Rows, long Entities,
    DateTimeOffset? LastMaterializedAt, DateTimeOffset? MaxEventTs, DateTimeOffset CreatedAt);
public sealed record PitRow(long Idx, string EntityKey, DateTimeOffset SpineTs, DateTimeOffset? EventTs, string? Values);
public sealed record OnlineRow(string EntityKey, DateTimeOffset EventTs, string Values);
public sealed record BindingRow(Guid DeploymentId, Guid WorkspaceId, Guid EntityId, string EntityName, string JoinKey, string Features, DateTimeOffset UpdatedAt);

public static partial class FeatureSql
{
    [SqlQuery("Feature/DeleteDeploymentBinding.sql")]
    public static partial Task DeleteDeploymentBindingAsync(this IWriteSession db, Guid deploymentId, CancellationToken ct);
    [SqlQuery("Feature/InsertEntity.sql")]
    public static partial Task InsertEntityAsync(this IWriteSession db, Guid id, Guid workspaceId, string name, string joinKey, string description, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Feature/ListEntities.sql")]
    public static partial Task<List<EntityRow>> ListEntitiesAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Feature/GetEntity.sql")]
    public static partial Task<EntityRow?> GetEntityAsync(this IReadSession db, Guid workspaceId, string key, CancellationToken ct);
    [SqlQuery("Feature/InsertView.sql")]
    public static partial Task<int> InsertViewAsync(this IWriteSession db, Guid id, Guid workspaceId, string name, Guid entityId, Guid sourceVersionId, string timestampColumn,
        string features, int ttlHours, bool online, int? materializeEveryMin, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Feature/GetView.sql")]
    public static partial Task<ViewRow?> GetViewAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Feature/ResolveView.sql")]
    public static partial Task<ViewRow?> ResolveViewAsync(this IReadSession db, Guid workspaceId, string name, int version, CancellationToken ct);
    [SqlQuery("Feature/ListViews.sql")]
    public static partial Task<List<ViewRow>> ListViewsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Feature/DueViews.sql")]
    public static partial Task<List<ViewRow>> DueViewsAsync(this IReadSession db, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Feature/ClearOffline.sql")]
    public static partial Task ClearOfflineAsync(this IWriteSession db, Guid viewId, CancellationToken ct);
    [SqlQuery("Feature/InsertOffline.sql")]
    public static partial Task InsertOfflineAsync(this IWriteSession db, Guid viewId, string[] keys, DateTimeOffset[] times, string[] vals, CancellationToken ct);
    [SqlQuery("Feature/RefreshOnline.sql")]
    public static partial Task<long> RefreshOnlineAsync(this IWriteSession db, Guid viewId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Feature/FinishMaterialize.sql")]
    public static partial Task FinishMaterializeAsync(this IWriteSession db, Guid id, string state, string? reason, long rows, long entities, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Feature/PointInTime.sql")]
    public static partial Task<List<PitRow>> PointInTimeAsync(this IReadSession db, string[] keys, DateTimeOffset[] times, Guid viewId, int ttlHours, CancellationToken ct);
    [SqlQuery("Feature/OnlineLookup.sql")]
    public static partial Task<List<OnlineRow>> OnlineLookupAsync(this IReadSession db, Guid viewId, string[] keys, CancellationToken ct);
    [SqlQuery("Feature/OnlineSample.sql")]
    public static partial Task<List<OnlineRow>> OnlineSampleAsync(this IReadSession db, Guid viewId, int limit, CancellationToken ct);
    [SqlQuery("Feature/UpsertBinding.sql")]
    public static partial Task UpsertBindingAsync(this IWriteSession db, Guid deploymentId, Guid workspaceId, Guid entityId, string features, Guid updatedBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Feature/GetBinding.sql")]
    public static partial Task<BindingRow?> GetBindingAsync(this IReadSession db, Guid deploymentId, CancellationToken ct);
}

/// <summary>Feature reference `view:feature` or `view@2:feature` (no version = latest ready version).</summary>
public sealed record FeatureRef(string View, int Version, string Feature)
{
    public static FeatureRef Parse(string s)
    {
        var colon = s.IndexOf(':');
        if (colon <= 0 || colon == s.Length - 1) throw new InvalidArgumentException("FEATURE_REF_INVALID", $"Reference '{s}' must be in the form view:feature");
        var view = s[..colon];
        var at = view.IndexOf('@');
        var version = 0;
        if (at > 0 && !int.TryParse(view[(at + 1)..], out version)) throw new InvalidArgumentException("FEATURE_REF_INVALID", $"Invalid version in '{s}'");
        return new FeatureRef(at > 0 ? view[..at] : view, version, s[(colon + 1)..]);
    }
}

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

/// <summary>
/// Materialize (M07 §5): reads the source dataset version's CSV → offline store (full history by event time) → online store (latest row per entity).
/// Fully rebuilt each time (idempotent). Data errors → view `failed` + reason, no throw (so the state is saved).
/// </summary>
internal static class Materializer
{
    public static async Task<ViewRow> RunAsync(IWriteSession db, S3Client s3, IClock clock, ViewRow v, CancellationToken ct)
    {
        var now = clock.UtcNow;
        try
        {
            var (header, rows) = await LoadSourceAsync(db, s3, v.WorkspaceId, v.SourceVersionId, ct);
            int Col(string n) => Array.IndexOf(header, n) is var i and >= 0 ? i : throw new FormatException($"Source is missing column '{n}'");
            var keyCol = Col(v.JoinKey);
            var tsCol = Col(v.TimestampColumn);
            var feats = JsonNode.Parse(v.Features)!.AsArray().Select(f => (Name: f!["name"]!.ToString(), Type: f["type"]!.ToString(), Index: Col(f["name"]!.ToString()))).ToList();
            var keys = new List<string>(); var times = new List<DateTimeOffset>(); var vals = new List<string>();
            await db.ClearOfflineAsync(v.Id, ct);
            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                var key = row.ElementAtOrDefault(keyCol) ?? "";
                if (key.Length == 0) throw new FormatException($"Row {r + 2}: missing {v.JoinKey}");
                if (!DateTimeOffset.TryParse(row.ElementAtOrDefault(tsCol), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts))
                    throw new FormatException($"Row {r + 2}: '{row.ElementAtOrDefault(tsCol)}' is not a timestamp (ISO 8601)");
                var o = new JsonObject();
                foreach (var f in feats)
                {
                    var raw = row.ElementAtOrDefault(f.Index) ?? "";
                    o[f.Name] = raw.Length == 0 ? null
                        : f.Type == "number" ? double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? JsonValue.Create(d) : throw new FormatException($"Row {r + 2}: {f.Name}='{raw}' is not a number")
                        : JsonValue.Create(raw);
                }
                keys.Add(key); times.Add(ts); vals.Add(o.ToJsonString());
                if (keys.Count == 5000) { await db.InsertOfflineAsync(v.Id, [.. keys], [.. times], [.. vals], ct); keys.Clear(); times.Clear(); vals.Clear(); }
            }
            if (keys.Count > 0) await db.InsertOfflineAsync(v.Id, [.. keys], [.. times], [.. vals], ct);
            var entities = v.Online ? await db.RefreshOnlineAsync(v.Id, now, ct) : rows.Select(r => r.ElementAtOrDefault(keyCol)).Distinct().LongCount();
            await db.FinishMaterializeAsync(v.Id, "ready", null, rows.Count, entities, now, ct);
        }
        catch (Exception e) when (e is FormatException or AppException)
        {
            await db.ClearOfflineAsync(v.Id, ct);
            await db.FinishMaterializeAsync(v.Id, "failed", e.Message, 0, 0, now, ct);
        }
        return (await db.GetViewAsync(v.Id, v.WorkspaceId, ct))!;
    }

    public static async Task<(string[] Header, List<string[]> Rows)> LoadSourceAsync(IReadSession db, S3Client s3, Guid ws, Guid versionId, CancellationToken ct)
    {
        var ver = await db.GetVersionAsync(versionId, ws, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Source dataset version not found");
        string[] header = [];
        var rows = new List<string[]>();
        long total = 0;
        foreach (var f in (await db.ListVersionFilesAsync(ver.Id, ct)).Where(f => f.Path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
        {
            total += f.SizeBytes;
            if (total > 50L * 1024 * 1024) throw new ResourceExhaustedException("FEATURE_SOURCE_TOO_LARGE", "Source CSV > 50 MB (Ray Data/Parquet later)");
            var (h, r) = Drift.ParseCsv(Encoding.UTF8.GetString(await s3.GetAsync(Buckets.Datasets, ver.Prefix + f.Path, ct) ?? []));
            if (header.Length == 0) header = h;
            if (h.SequenceEqual(header)) rows.AddRange(r);
        }
        if (header.Length == 0) throw new PreconditionException("FEATURE_SOURCE_UNSUPPORTED", "Dataset version has no tabular CSV");
        return (header, rows);
    }
}

// ───────────── Entity + view ─────────────

public sealed record CreateEntityCommand(Guid WorkspaceId, string Name, string JoinKey, string Description) : ICommand<EntityRow>;

public sealed class CreateEntityCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreateEntityCommand, EntityRow>
{
    public async ValueTask<EntityRow> Handle(CreateEntityCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        if (!Identity.IdentityRules.Slug().IsMatch(c.Name)) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Name may only contain lowercase letters, numbers and hyphens" });
        if (string.IsNullOrWhiteSpace(c.JoinKey)) throw new ValidationException(new Dictionary<string, string> { ["join_key"] = "A key column is required (e.g. customer_id)" });
        var id = Ids.New();
        try { await db.InsertEntityAsync(id, c.WorkspaceId, c.Name, c.JoinKey.Trim(), c.Description ?? "", uid, clock.UtcNow, ct); }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("FEATURE_ENTITY_NAME_TAKEN", "Entity name is already in use"); }
        return (await db.GetEntityAsync(c.WorkspaceId, id.ToString(), ct))!;
    }
}

public sealed record ListEntitiesQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<EntityRow>>;

public sealed class ListEntitiesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListEntitiesQuery, IReadOnlyList<EntityRow>>
{
    public async ValueTask<IReadOnlyList<EntityRow>> Handle(ListEntitiesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListEntitiesAsync(q.WorkspaceId, ct);
    }
}

/// <summary>Creates a feature view (same name → new version; the old one is kept so earlier training stays reproducible), then materializes it immediately.</summary>
public sealed record CreateFeatureViewCommand(Guid WorkspaceId, string Name, string Entity, Guid SourceVersionId, string TimestampColumn,
    IReadOnlyList<string> Features, int TtlHours, bool Online, int? MaterializeEveryMin) : ICommand<ViewRow>;

public sealed class CreateFeatureViewCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, S3Client s3) : ICommandHandler<CreateFeatureViewCommand, ViewRow>
{
    public async ValueTask<ViewRow> Handle(CreateFeatureViewCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        if (!Identity.IdentityRules.Slug().IsMatch(c.Name)) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Name may only contain lowercase letters, numbers and hyphens" });
        var entity = await db.GetEntityAsync(c.WorkspaceId, c.Entity, ct) ?? throw new NotFoundException("FEATURE_ENTITY_NOT_FOUND", "Entity not found");
        var (header, rows) = await Materializer.LoadSourceAsync(db, s3, c.WorkspaceId, c.SourceVersionId, ct);
        var ts = string.IsNullOrWhiteSpace(c.TimestampColumn) ? "event_time" : c.TimestampColumn.Trim();
        foreach (var need in new[] { entity.JoinKey, ts })
            if (!header.Contains(need)) throw new InvalidArgumentException("FEATURE_SOURCE_COLUMN_MISSING", $"Source is missing column '{need}' (available: {string.Join(", ", header)})");
        var names = c.Features.Count > 0 ? c.Features.ToList() : header.Where(h => h != entity.JoinKey && h != ts).ToList();
        var unknown = names.Where(n => !header.Contains(n)).ToList();
        if (unknown.Count > 0) throw new InvalidArgumentException("FEATURE_SOURCE_COLUMN_MISSING", "Source has no columns: " + string.Join(", ", unknown));
        // Type inferred from data: every non-empty value is numeric → number, otherwise string.
        var defs = new JsonArray(names.Select(n =>
        {
            var i = Array.IndexOf(header, n);
            var numeric = rows.Select(r => r.ElementAtOrDefault(i) ?? "").Where(x => x.Length > 0)
                .All(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
            return (JsonNode)new JsonObject { ["name"] = n, ["type"] = numeric ? "number" : "string" };
        }).ToArray());
        var id = Ids.New();
        await db.InsertViewAsync(id, c.WorkspaceId, c.Name, entity.Id, c.SourceVersionId, ts, defs.ToJsonString(), c.TtlHours is > 0 and <= 24 * 365 * 10 ? c.TtlHours : 24 * 30,
            c.Online, c.MaterializeEveryMin is >= 5 ? c.MaterializeEveryMin : null, uid, clock.UtcNow, ct);
        return await Materializer.RunAsync(db, s3, clock, (await db.GetViewAsync(id, c.WorkspaceId, ct))!, ct);
    }
}

public sealed record ListFeatureViewsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ViewRow>>;

public sealed class ListFeatureViewsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListFeatureViewsQuery, IReadOnlyList<ViewRow>>
{
    public async ValueTask<IReadOnlyList<ViewRow>> Handle(ListFeatureViewsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListViewsAsync(q.WorkspaceId, ct);
    }
}

public sealed record MaterializeViewCommand(Guid WorkspaceId, Guid ViewId) : ICommand<ViewRow>;

public sealed class MaterializeViewCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, S3Client s3) : ICommandHandler<MaterializeViewCommand, ViewRow>
{
    public async ValueTask<ViewRow> Handle(MaterializeViewCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        var v = await db.GetViewAsync(c.ViewId, c.WorkspaceId, ct) ?? throw new NotFoundException("FEATURE_VIEW_NOT_FOUND", "Feature view not found");
        return await Materializer.RunAsync(db, s3, clock, v, ct);
    }
}

/// <summary>Background loop: materializes views whose schedule is due.</summary>
public sealed record MaterializeDueCommand : ICommand<int>, IInternalCommand;

public sealed class MaterializeDueCommandHandler(IWriteSession db, IClock clock, S3Client s3) : ICommandHandler<MaterializeDueCommand, int>
{
    public async ValueTask<int> Handle(MaterializeDueCommand c, CancellationToken ct)
    {
        var due = await db.DueViewsAsync(clock.UtcNow, ct);
        foreach (var v in due) await Materializer.RunAsync(db, s3, clock, v, ct);
        return due.Count;
    }
}

// ───────────── Retrieval ─────────────

public sealed record FeatureTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<JsonNode?>> Rows);

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

public sealed record OnlineValue(string Key, IReadOnlyDictionary<string, JsonNode?> Values, IReadOnlyList<string> Missing, DateTimeOffset? OldestEventTs);

/// <summary>Online: latest value per entity; past TTL counts as missing (stale values are never returned silently).</summary>
public sealed record GetOnlineFeaturesQuery(Guid WorkspaceId, IReadOnlyList<string> Keys, IReadOnlyList<string> Refs) : IQuery<IReadOnlyList<OnlineValue>>;

public sealed class GetOnlineFeaturesQueryHandler(IReadSession db, IClock clock, WorkspaceAuthorizer auth) : IQueryHandler<GetOnlineFeaturesQuery, IReadOnlyList<OnlineValue>>
{
    public async ValueTask<IReadOnlyList<OnlineValue>> Handle(GetOnlineFeaturesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await OnlineFeatures.LookupAsync(db, clock, q.WorkspaceId, q.Keys, q.Refs, null, ct);
    }
}

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

public sealed record OnlineSampleQuery(Guid WorkspaceId, Guid ViewId, int Limit) : IQuery<IReadOnlyList<OnlineRow>>;

public sealed class OnlineSampleQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<OnlineSampleQuery, IReadOnlyList<OnlineRow>>
{
    public async ValueTask<IReadOnlyList<OnlineRow>> Handle(OnlineSampleQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetViewAsync(q.ViewId, q.WorkspaceId, ct) ?? throw new NotFoundException("FEATURE_VIEW_NOT_FOUND", "Feature view not found");
        return await db.OnlineSampleAsync(q.ViewId, q.Limit is > 0 and <= 500 ? q.Limit : 50, ct);
    }
}

// ───────────── Serving ─────────────

/// <summary>Binds features to a deployment: the gateway receives `{"entity":"c1"}` → fetches exactly these features from the online store (same definitions as at training time).</summary>
public sealed record BindDeploymentFeaturesCommand(Guid WorkspaceId, Guid DeploymentId, string Entity, IReadOnlyList<string> Refs) : ICommand<BindingRow>;

public sealed class BindDeploymentFeaturesCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<BindDeploymentFeaturesCommand, BindingRow>
{
    public async ValueTask<BindingRow> Handle(BindDeploymentFeaturesCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        _ = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        var entity = await db.GetEntityAsync(c.WorkspaceId, c.Entity, ct) ?? throw new NotFoundException("FEATURE_ENTITY_NOT_FOUND", "Entity not found");
        foreach (var (view, _) in await FeatureLookup.ResolveAsync(db, c.WorkspaceId, c.Refs, entity.Id, ct))
            if (!view.Online) throw new PreconditionException("FEATURE_VIEW_NOT_ONLINE", $"View {view.Name} does not have online enabled");
        await db.UpsertBindingAsync(c.DeploymentId, c.WorkspaceId, entity.Id, new JsonArray(c.Refs.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()).ToJsonString(), uid, clock.UtcNow, ct);
        return (await db.GetBindingAsync(c.DeploymentId, ct))!;
    }
}

public sealed record GetDeploymentFeaturesQuery(Guid WorkspaceId, Guid DeploymentId) : IQuery<BindingRow?>;

public sealed class GetDeploymentFeaturesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetDeploymentFeaturesQuery, BindingRow?>
{
    public async ValueTask<BindingRow?> Handle(GetDeploymentFeaturesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.GetBindingAsync(q.DeploymentId, ct);
    }
}

/// <summary>Gateway (no user): feature values for entities per the deployment's binding.</summary>
public sealed record ResolveServingFeaturesQuery(DeploymentRow Deployment, IReadOnlyList<string> Keys) : IQuery<IReadOnlyList<OnlineValue>?>;

public sealed class ResolveServingFeaturesQueryHandler(IReadSession db, IClock clock) : IQueryHandler<ResolveServingFeaturesQuery, IReadOnlyList<OnlineValue>?>
{
    public async ValueTask<IReadOnlyList<OnlineValue>?> Handle(ResolveServingFeaturesQuery q, CancellationToken ct)
    {
        var b = await db.GetBindingAsync(q.Deployment.Id, ct);
        if (b is null) return null;
        var refs = JsonNode.Parse(b.Features)!.AsArray().Select(x => x!.ToString()).ToList();
        return await OnlineFeatures.LookupAsync(db, clock, q.Deployment.WorkspaceId, q.Keys, refs, b.EntityId, ct);
    }
}
