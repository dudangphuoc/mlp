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

/// <summary>Creates a feature view (same name → new version; the old one is kept so earlier training stays
/// reproducible), then materializes it immediately.</summary>
[RequiresPermission("dataset.write")]
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
