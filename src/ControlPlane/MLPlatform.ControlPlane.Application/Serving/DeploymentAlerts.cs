using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Serving;

internal static class DeploymentAlerts
{
    public static async Task SendAsync(IWriteSession db, IClock clock, Guid deploymentId, string kind, string what, string severity, CancellationToken ct)
    {
        if (await MLPlatform.ControlPlane.Application.Notifications.NotifySql.DeploymentInfoAsync(db, deploymentId, ct) is not { } d) return;
        var to = (await MLPlatform.ControlPlane.Application.Notifications.Notifier.AdminsAsync(db, d.WorkspaceId, ct)).Append(d.CreatedBy);
        await MLPlatform.ControlPlane.Application.Notifications.Notifier.SendAsync(db, clock, d.WorkspaceId, to, kind,
            $"Deployment {d.Slug} {what}", "#/deployments", $"deployment/{d.Id}", severity, ct);
    }
}
