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
