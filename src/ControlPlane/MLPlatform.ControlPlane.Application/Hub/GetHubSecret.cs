using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>HF token secret for the api to call the Hub (internal — not exposed over RPC).</summary>
[Access(AccessKind.Internal)]
public sealed record GetHubSecretQuery(Guid WorkspaceId) : IQuery<string?>;

/// <summary>Handles <see cref="GetHubSecretQuery"/>.</summary>
public sealed class GetHubSecretQueryHandler(IReadSession db) : IQueryHandler<GetHubSecretQuery, string?>
{
    public async ValueTask<string?> Handle(GetHubSecretQuery q, CancellationToken ct) => (await db.GetIntegrationAsync(q.WorkspaceId, "huggingface", ct))?.SecretEnc;
}
