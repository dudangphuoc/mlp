using MLPlatform.ControlPlane.Infrastructure;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Grpc;

public sealed class NotificationGrpc(IMediator mediator, TimeProvider time, MLPlatform.ControlPlane.Infrastructure.StreamBroker broker) : NotificationService.NotificationServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    public override async Task<ListNotificationsResponse> ListNotifications(ListNotificationsRequest r, ServerCallContext ctx)
    {
        var (items, unread) = await mediator.Query(new ListNotificationsQuery(G(r.WorkspaceId, "workspace_id"), r.UnreadOnly), ctx.CancellationToken);
        var res = new ListNotificationsResponse { Unread = unread };
        res.Notifications.AddRange(items.Select(ToProto));
        return res;
    }

    /// <summary>Wakes when StreamBroker signals a workspace change (NOTIFY, correct even with multiple api replicas),
    /// re-polls at least every 5 s; sends records changed after the previous mark.</summary>
    public override async Task WatchNotifications(WatchNotificationsRequest r, IServerStreamWriter<NotificationEvent> stream, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        var since = time.GetUtcNow();
        var (_, unread) = await mediator.Query(new NotificationsChangedQuery(ws, since), ctx.CancellationToken);
        await stream.WriteAsync(new NotificationEvent { Unread = unread }, ctx.CancellationToken);
        var lastUnread = unread;
        var sent = new Dictionary<Guid, DateTimeOffset>();
        try
        {
            while (true)
            {
                await broker.WaitAsync([MLPlatform.ControlPlane.Infrastructure.StreamBroker.Ws(ws)], TimeSpan.FromSeconds(5), ctx.CancellationToken);
                var (rows, n) = await mediator.Query(new NotificationsChangedQuery(ws, since.AddSeconds(-5)), ctx.CancellationToken);
                var items = rows.Where(it => !sent.TryGetValue(it.Id, out var at) || at != it.UpdatedAt).ToList();
                foreach (var it in items)
                {
                    await stream.WriteAsync(new NotificationEvent { Notification = ToProto(it), Unread = n }, ctx.CancellationToken);
                    sent[it.Id] = it.UpdatedAt;
                    if (it.UpdatedAt > since) since = it.UpdatedAt;
                }
                if (sent.Count > 500) sent.Clear();
                if (items.Count == 0 && n != lastUnread)
                    await stream.WriteAsync(new NotificationEvent { Unread = n }, ctx.CancellationToken);
                lastUnread = n;
            }
        }
        catch (OperationCanceledException) { }
    }

    public override async Task<MarkReadResponse> MarkRead(MarkReadRequest r, ServerCallContext ctx) =>
        new() { Unread = await mediator.Send(new MarkNotificationsReadCommand(G(r.WorkspaceId, "workspace_id"), r.Ids.Select(i => G(i, "ids")).ToList(), r.All), ctx.CancellationToken) };

    public override async Task<NotificationPreferences> GetPreferences(GetNotificationPreferencesRequest r, ServerCallContext ctx) =>
        await Prefs(G(r.WorkspaceId, "workspace_id"), ctx);

    public override async Task<NotificationPreferences> UpdatePreferences(UpdateNotificationPreferencesRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        await mediator.Send(new UpdateNotificationPreferencesCommand(ws, r.Preferences.Select(p => (p.Kind, p.InApp)).ToList()), ctx.CancellationToken);
        return await Prefs(ws, ctx);
    }

    private async Task<NotificationPreferences> Prefs(Guid ws, ServerCallContext ctx)
    {
        var res = new NotificationPreferences();
        res.Preferences.AddRange((await mediator.Query(new GetNotificationPreferencesQuery(ws), ctx.CancellationToken)).Select(p => new NotificationPreference
            { Kind = p.Kind, Group = p.Group, Label = p.Label, Mandatory = p.Mandatory, InApp = p.Enabled }));
        return res;
    }

    private static Notification ToProto(NotificationRow n) => new()
    {
        Id = n.Id.ToString(), Kind = n.Kind, Title = n.Title, Link = n.Link, ResourceUri = n.ResourceUri, Severity = n.Severity, Count = n.Count,
        CreatedAt = Timestamp.FromDateTimeOffset(n.CreatedAt), UpdatedAt = Timestamp.FromDateTimeOffset(n.UpdatedAt), Read = n.ReadAt is not null,
    };
}
