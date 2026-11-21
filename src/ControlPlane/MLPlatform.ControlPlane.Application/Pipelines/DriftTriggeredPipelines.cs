using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Pipelines;

/// <summary>Runs every pipeline of the workspace that has a deployment.drift event trigger, with the deployment as a
/// parameter.</summary>
public sealed class DriftTriggeredPipelines(IWriteSession db, IClock clock) : IDriftListener
{
    public Task OnDriftAlertAsync(Guid workspaceId, JsonObject deployment, CancellationToken ct) =>
        PipelineTriggers.FireAsync(db, clock, "deployment.drift", workspaceId, deployment, ct);
}
