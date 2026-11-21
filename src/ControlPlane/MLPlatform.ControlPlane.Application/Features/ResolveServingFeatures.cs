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
