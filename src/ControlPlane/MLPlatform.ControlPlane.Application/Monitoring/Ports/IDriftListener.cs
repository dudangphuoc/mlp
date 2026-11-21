using System.Text.Json.Nodes;

namespace MLPlatform.ControlPlane.Application.Monitoring;

/// <summary>Reacts when a deployment's data drift turns into an alert (continuous training starts pipelines here).</summary>
public interface IDriftListener
{
    Task OnDriftAlertAsync(Guid workspaceId, JsonObject deployment, CancellationToken ct);
}
