using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

public sealed record UpdateNotificationPreferencesCommand(Guid WorkspaceId, IReadOnlyList<(string Kind, bool Enabled)> Changes) : ICommand<bool>;

public sealed class UpdateNotificationPreferencesCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<UpdateNotificationPreferencesCommand, bool>
{
    public async ValueTask<bool> Handle(UpdateNotificationPreferencesCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.read", ct);
        foreach (var (kind, enabled) in c.Changes)
        {
            var k = NotificationKinds.Find(kind) ?? throw new ValidationException(new Dictionary<string, string> { ["kind"] = $"Unknown kind {kind}" });
            if (k.Mandatory && !enabled) throw new InvalidArgumentException("NOTIFY_KIND_MANDATORY", "This notification kind cannot be turned off");
            await db.UpsertPreferenceAsync(uid, c.WorkspaceId, kind, enabled, ct);
        }
        return true;
    }
}
