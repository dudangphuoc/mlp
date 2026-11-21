using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

/// <summary>A recipe image on one machine: <c>building</c>, <c>ready</c> or <c>failed</c> for one image tag.</summary>
public sealed record EnvironmentRow(Guid Id, Guid WorkspaceId, string RecipeName, string EnvTag, Guid HostId, string State, string Error,
    DateTimeOffset StartedAt, DateTimeOffset? BuiltAt);

/// <summary>
/// A cached CPU-only inspection of a recipe; <c>Blocks</c> is a JSON array of <c>{key, params, trainable}</c>, <c>Sources</c> a JSON object
/// pane → <c>[{name, file, line, code}]</c> (the upstream code the merged config selects).
/// </summary>
public sealed record InspectionRow(Guid Id, Guid WorkspaceId, string RecipeName, string EnvTag, string Variant, string ParamsSha256,
    int NumClasses, string State, string Error, string OverlayYaml, string ConfigJson, string Blocks, DateTimeOffset CreatedAt, string Sources);

/// <summary>What an inspection is cached by: the image, the variant, the parameters that change the result, and the class count.</summary>
public sealed record InspectionKey(Guid WorkspaceId, string EnvTag, string Variant, string ParamsSha256, int NumClasses);

/// <summary>What the inspection container reported: the overlay, the merged config and the model blocks, or why there is none.</summary>
public sealed record InspectionOutput(bool Ok, string Error, string OverlayYaml, string ConfigJson, string BlocksJson, string SourcesJson = "{}");

/// <summary>
/// Rules of the recipe inspection (round 1 slice 2, D-44): which parameters make a new cache entry, the command the image runs
/// (<c>canvas.inspect</c> of the manifest) and how its one <c>MLP_INSPECT {json}</c> line is read.
/// </summary>
public static class InspectionRules
{
    public const string Marker = "MLP_INSPECT ";
    /// <summary>The agent keeps the last 64 KB of stdout; an output near that size has lost its beginning.</summary>
    private const int OutputLimit = 65536;
    public static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan BuildLimit = TimeSpan.FromMinutes(70);

    /// <summary>The values a person set: a JSON object without nulls (null = "as the variant has it"). Empty text = no values.</summary>
    public static JsonObject ParseParams(string json)
    {
        var res = new JsonObject();
        if (string.IsNullOrWhiteSpace(json)) return res;
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { node = null; }
        if (node is not JsonObject o) throw new ValidationException(new Dictionary<string, string> { ["params_json"] = "Must be a JSON object" });
        foreach (var (k, v) in o.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (v is not null) res[k] = v.DeepClone();
        return res;
    }

    /// <summary>SHA-256 of the canonical JSON (keys in ordinal order) of the set parameters the manifest marks <c>affectsInspect: true</c>.</summary>
    public static string ParamsHash(JsonObject manifest, JsonObject set)
    {
        var defs = manifest["params"] as JsonObject;
        var canon = new JsonObject();
        foreach (var (k, v) in set.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (v is not null && defs?[k]?["affectsInspect"] is JsonValue f && f.TryGetValue<bool>(out var on) && on) canon[k] = v.DeepClone();
        // "v2": inspections made before the defaults were passed (2026-11-20) showed the variant's own schedule — never reuse them.
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("v2:" + canon.ToJsonString())));
    }

    public static JsonObject? Variant(JsonObject manifest, string name) =>
        (manifest["variants"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(v => v["name"]?.ToString() == name);

    /// <summary>
    /// The shell command of <c>canvas.inspect</c>: <c>{variant.x}</c> from the variant, <c>{dataset.classes.count}</c> = the class count
    /// (0 = keep the variant's own), <c>{params.json}</c> = every set value, <c>{params.x}</c> = the set value or the manifest default.
    /// </summary>
    /// <summary>
    /// The values a training would run with: what was set, plus every non-null manifest default. Without the defaults the inspection would
    /// show the variant's own schedule (e.g. 132 epochs) while the training scales it to the recipe default (72).
    /// </summary>
    public static JsonObject WithDefaults(JsonObject manifest, JsonObject set)
    {
        var res = new JsonObject();
        foreach (var (k, def) in manifest["params"] as JsonObject ?? [])
            if (def is JsonObject d && d["from"] is null && (set[k] ?? d["default"]) is { } val) res[k] = val.DeepClone();
        return res;
    }

    public static string Command(JsonObject manifest, JsonObject variant, JsonObject set, int numClasses)
    {
        var template = manifest["canvas"]?["inspect"]?.ToString() ?? "";
        return Template.Render(template.Replace('\n', ' ').Trim(), (string n, out JsonNode? v) =>
        {
            v = null;
            if (n == "dataset.classes.count") v = JsonValue.Create(numClasses);
            else if (n == "params.json") v = JsonValue.Create(WithDefaults(manifest, set).ToJsonString());
            else if (n.StartsWith("variant.", StringComparison.Ordinal))
            {
                if (!variant.ContainsKey(n[8..])) return false;
                v = variant[n[8..]]?.DeepClone();
            }
            else if (n.StartsWith("params.", StringComparison.Ordinal))
            {
                if (manifest["params"]?[n[7..]] is not JsonObject def) return false;
                v = (set[n[7..]] ?? def["default"])?.DeepClone();
            }
            else return false;
            return true;
        });
    }

    /// <summary>Reads the last <c>MLP_INSPECT</c> line of stdout. <paramref name="stderr"/> explains a failed run.</summary>
    public static InspectionOutput Parse(string stdout, bool exitOk, string stderr, Func<IReadOnlyList<string>, string?> headline)
    {
        static InspectionOutput Fail(string error) => new(false, error.Length > 500 ? error[..500] + "…" : error, "", "", "[]");
        if (!exitOk)
        {
            var lines = (stderr + "\n" + stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Fail(headline(lines) ?? lines.LastOrDefault(l => !l.StartsWith(Marker, StringComparison.Ordinal)) ?? "The inspection exited with an error");
        }
        var line = stdout.Split('\n').LastOrDefault(l => l.StartsWith(Marker, StringComparison.Ordinal));
        if (line is null)
            return Fail(stdout.Length >= OutputLimit - 1024 ? "inspection output too large" : "The inspection printed no MLP_INSPECT line");
        JsonObject? doc;
        try { doc = JsonNode.Parse(line[Marker.Length..].Trim()) as JsonObject; }
        catch (JsonException) { doc = null; }
        if (doc is null) return Fail("inspection output too large");
        var blocks = new JsonArray();
        foreach (var b in (doc["blocks"] as JsonArray)?.OfType<JsonObject>() ?? [])
            blocks.Add((JsonNode)new JsonObject { ["key"] = b["key"]?.ToString() ?? "", ["params"] = Long(b["params"]), ["trainable"] = Long(b["trainable"]) });
        return new InspectionOutput(true, "", doc["overlay_yaml"]?.ToString() ?? "", doc["config"]?.ToJsonString() ?? "{}", blocks.ToJsonString(),
            Sources(doc["sources_gz"]?.ToString()));
    }

    /// <summary>
    /// <c>sources_gz</c> = base64 of gzipped JSON <c>{pane: [{name, file, line, code}]}</c>. Only that shape is kept; anything unreadable
    /// gives <c>{}</c> (the canvas then has no Code level for the pane).
    /// </summary>
    public static string Sources(string? gz)
    {
        if (string.IsNullOrEmpty(gz)) return "{}";
        try
        {
            using var input = new MemoryStream(Convert.FromBase64String(gz));
            using var unzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var limited = new MemoryStream();
            var buf = new byte[81920];
            int n;
            while ((n = unzip.Read(buf, 0, buf.Length)) > 0)
            {
                limited.Write(buf, 0, n);
                if (limited.Length > 2_000_000) return "{}";
            }
            if (JsonNode.Parse(limited.ToArray()) is not JsonObject doc) return "{}";
            var res = new JsonObject();
            foreach (var (pane, list) in doc)
            {
                var items = new JsonArray();
                foreach (var s in (list as JsonArray)?.OfType<JsonObject>() ?? [])
                    items.Add((JsonNode)new JsonObject
                    {
                        ["name"] = s["name"]?.ToString() ?? "", ["file"] = s["file"]?.ToString() ?? "", ["line"] = Long(s["line"]), ["code"] = s["code"]?.ToString() ?? "",
                    });
                if (items.Count > 0) res[pane] = items;
            }
            return res.ToJsonString();
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or JsonException) { return "{}"; }
    }

    public static long Long(JsonNode? n) =>
        n is JsonValue v ? v.TryGetValue<long>(out var l) ? l : v.TryGetValue<double>(out var d) ? (long)d : 0 : 0;
}

/// <summary>Checks one workspace permission for the catalog RPCs whose work happens outside the handlers (machines, files on disk).</summary>
public sealed record RequireCatalogPermissionQuery(Guid WorkspaceId, string Permission) : IQuery<bool>;

/// <summary>Throws when the caller is not a member or lacks the permission.</summary>
public sealed class RequireCatalogPermissionQueryHandler(WorkspaceAuthorizer auth) : IQueryHandler<RequireCatalogPermissionQuery, bool>
{
    public async ValueTask<bool> Handle(RequireCatalogPermissionQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, q.Permission, ct);
        return true;
    }
}

/// <summary>Internal (after the RPC checked the permission): the recorded environments of a recipe in a workspace.</summary>
public sealed record ListEnvironmentRowsQuery(Guid WorkspaceId, string RecipeName) : IQuery<IReadOnlyList<EnvironmentRow>>;

/// <summary>A build that has been running longer than the limit was lost with an api restart and reads as failed.</summary>
public sealed class ListEnvironmentRowsQueryHandler(IReadSession db, IClock clock) : IQueryHandler<ListEnvironmentRowsQuery, IReadOnlyList<EnvironmentRow>>
{
    public async ValueTask<IReadOnlyList<EnvironmentRow>> Handle(ListEnvironmentRowsQuery q, CancellationToken ct) =>
        [.. (await db.ListEnvironmentsAsync(q.WorkspaceId, q.RecipeName, ct)).Select(e => Settle(e, clock.UtcNow))];

    public static EnvironmentRow Settle(EnvironmentRow e, DateTimeOffset now) =>
        e.State == "building" && now - e.StartedAt > InspectionRules.BuildLimit ? e with { State = "failed", Error = "interrupted" } : e;
}

/// <summary>Internal: the environment of one image tag on one machine, or null when it was never built or checked from here.</summary>
public sealed record GetEnvironmentRowQuery(Guid WorkspaceId, string EnvTag, Guid HostId) : IQuery<EnvironmentRow?>;

/// <summary>Same staleness rule as the list.</summary>
public sealed class GetEnvironmentRowQueryHandler(IReadSession db, IClock clock) : IQueryHandler<GetEnvironmentRowQuery, EnvironmentRow?>
{
    public async ValueTask<EnvironmentRow?> Handle(GetEnvironmentRowQuery q, CancellationToken ct) =>
        await db.GetEnvironmentAsync(q.WorkspaceId, q.EnvTag, q.HostId, ct) is { } e ? ListEnvironmentRowsQueryHandler.Settle(e, clock.UtcNow) : null;
}

/// <summary>
/// Internal (launcher, environment builds): records the state of an image on a machine. <c>building</c> starts the clock;
/// <c>ready</c> / <c>failed</c> on a machine with no record yet also creates one (the launcher built or found the image itself).
/// </summary>
public sealed record RecordEnvironmentCommand(Guid WorkspaceId, string RecipeName, string EnvTag, Guid HostId, string State, string Error) : ICommand<bool>;

/// <summary>Upserts on (workspace, tag, machine); finishing a build keeps its start time.</summary>
public sealed class RecordEnvironmentCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordEnvironmentCommand, bool>
{
    public async ValueTask<bool> Handle(RecordEnvironmentCommand c, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var error = c.Error.Length > 2000 ? c.Error[^2000..] : c.Error;
        DateTimeOffset? built = c.State == "ready" ? now : null;
        if (c.State != "building" && await db.GetEnvironmentAsync(c.WorkspaceId, c.EnvTag, c.HostId, ct) is not null)
            await db.FinishEnvironmentAsync(c.WorkspaceId, c.EnvTag, c.HostId, c.State, error, built, ct);
        else
            await db.UpsertEnvironmentAsync(Ids.New(), c.WorkspaceId, c.RecipeName, c.EnvTag, c.HostId, c.State, error, now, built, ct);
        return true;
    }
}

/// <summary>Internal: the cached inspection for a key, or null.</summary>
public sealed record GetInspectionRowQuery(InspectionKey Key) : IQuery<InspectionRow?>;

/// <summary>An inspection that has been running longer than its limit was lost and reads as failed.</summary>
public sealed class GetInspectionRowQueryHandler(IReadSession db, IClock clock) : IQueryHandler<GetInspectionRowQuery, InspectionRow?>
{
    public async ValueTask<InspectionRow?> Handle(GetInspectionRowQuery q, CancellationToken ct)
    {
        var k = q.Key;
        var r = await db.GetInspectionAsync(k.WorkspaceId, k.EnvTag, k.Variant, k.ParamsSha256, k.NumClasses, ct);
        return r is { State: "running" } && clock.UtcNow - r.CreatedAt > InspectionRules.RunLimit + TimeSpan.FromSeconds(45)
            ? r with { State = "failed", Error = "interrupted" } : r;
    }
}

/// <summary>Internal: marks an inspection as running (a previous result for the same key is cleared) and returns the row.</summary>
public sealed record StartInspectionCommand(InspectionKey Key, string RecipeName) : ICommand<InspectionRow>;

/// <summary>One row per key; starting again resets it.</summary>
public sealed class StartInspectionCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<StartInspectionCommand, InspectionRow>
{
    public async ValueTask<InspectionRow> Handle(StartInspectionCommand c, CancellationToken ct)
    {
        var k = c.Key;
        await db.StartInspectionAsync(Ids.New(), k.WorkspaceId, c.RecipeName, k.EnvTag, k.Variant, k.ParamsSha256, k.NumClasses, clock.UtcNow, ct);
        return (await db.GetInspectionAsync(k.WorkspaceId, k.EnvTag, k.Variant, k.ParamsSha256, k.NumClasses, ct))!;
    }
}

/// <summary>Internal: stores what the inspection container reported (ready with its result, or failed with the reason).</summary>
public sealed record FinishInspectionCommand(InspectionKey Key, InspectionOutput Output) : ICommand<bool>;

/// <summary>Writes the result onto the running row.</summary>
public sealed class FinishInspectionCommandHandler(IWriteSession db) : ICommandHandler<FinishInspectionCommand, bool>
{
    public async ValueTask<bool> Handle(FinishInspectionCommand c, CancellationToken ct)
    {
        var (k, o) = (c.Key, c.Output);
        await db.FinishInspectionAsync(k.WorkspaceId, k.EnvTag, k.Variant, k.ParamsSha256, k.NumClasses, o.Ok ? "ready" : "failed", o.Error,
            o.OverlayYaml, o.ConfigJson, o.BlocksJson, o.SourcesJson, ct);
        return true;
    }
}

/// <summary>Internal: forgets an inspection that could not run because the image is gone from the machine.</summary>
public sealed record DeleteInspectionCommand(InspectionKey Key) : ICommand<bool>;

/// <summary>Removes the row so the next request starts from the environment again.</summary>
public sealed class DeleteInspectionCommandHandler(IWriteSession db) : ICommandHandler<DeleteInspectionCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteInspectionCommand c, CancellationToken ct)
    {
        var k = c.Key;
        await db.DeleteInspectionAsync(k.WorkspaceId, k.EnvTag, k.Variant, k.ParamsSha256, k.NumClasses, ct);
        return true;
    }
}
