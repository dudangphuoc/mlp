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

/// <summary>Materialize (M07 §5): reads the source dataset version's CSV → offline store (full history by event time) →
/// online store (latest row per entity).</summary>
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
                var o = ParseFeatureValues(row, r, feats);
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

    private static JsonObject ParseFeatureValues(string[] row, int r, List<(string Name, string Type, int Index)> feats)
    {
        var o = new JsonObject();
        foreach (var f in feats)
        {
            var raw = row.ElementAtOrDefault(f.Index) ?? "";
            o[f.Name] = raw.Length == 0 ? null
                : f.Type == "number" ? double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? JsonValue.Create(d) : throw new FormatException($"Row {r + 2}: {f.Name}='{raw}' is not a number")
                : JsonValue.Create(raw);
        }
        return o;
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
