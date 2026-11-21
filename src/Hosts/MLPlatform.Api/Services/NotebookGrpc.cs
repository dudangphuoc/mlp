using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Infrastructure;
using MLPlatform.ControlPlane.Application.Notebooks;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Services;

public sealed class NotebookGrpc(IMediator mediator, WorkloadLauncher launcher) : NotebookService.NotebookServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    private static Timestamp? Ts(DateTimeOffset? t) => t is { } v ? Timestamp.FromDateTimeOffset(v) : null;

    public override async Task<ListNotebookProfilesResponse> ListProfiles(ListNotebookProfilesRequest r, ServerCallContext ctx)
    {
        var res = new ListNotebookProfilesResponse();
        res.Profiles.AddRange((await mediator.Query(new ListProfilesQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(p => new NotebookProfile
            { Name = p.Name, DisplayName = p.DisplayName, Description = p.Description, ImageCpu = p.ImageCpu, ImageGpu = p.ImageGpu, Version = p.Version }));
        return res;
    }

    public override async Task<NotebookServer> CreateServer(CreateNotebookRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreateNotebookCommand(G(r.WorkspaceId, "workspace_id"), r.Name, r.Profile, r.Ide, r.Cpus, r.MemoryGb, r.Gpus,
            r.IdleTimeoutMin, r.ImageOverride, r.Network, Guid.TryParse(r.HostId, out var hid) ? hid : null), ctx.CancellationToken));

    public override async Task<ListNotebooksResponse> ListServers(ListNotebooksRequest r, ServerCallContext ctx)
    {
        var res = new ListNotebooksResponse();
        res.Servers.AddRange((await mediator.Query(new ListNotebooksQuery(G(r.WorkspaceId, "workspace_id"), r.All), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<NotebookServer> GetServer(NotebookRef r, ServerCallContext ctx) =>
        ToProto(await mediator.Query(new GetNotebookQuery(G(r.WorkspaceId, "workspace_id"), G(r.ServerId, "server_id")), ctx.CancellationToken));

    public override async Task<NotebookServer> StopServer(NotebookRef r, ServerCallContext ctx)
    {
        var s = await mediator.Send(new StopNotebookCommand(G(r.WorkspaceId, "workspace_id"), G(r.ServerId, "server_id")), ctx.CancellationToken);
        // The stop goes to the machine in the background: the user gets an answer at once, and a machine that does not answer
        // cannot turn the request into an error or a hang (the reaper finishes "stopping" notebooks after 5 minutes).
        if (s.State is "stopping" or "stopped" && s.JobId is { } job)
            _ = Task.Run(async () =>
            {
                try { await launcher.StopJobAsync(job, CancellationToken.None); }
                catch (Exception e) when (e is TimeoutException or InvalidOperationException or IOException or ObjectDisposedException) { }
            });
        return ToProto(s);
    }

    public override async Task<NotebookServer> ExtendIdle(NotebookRef r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new ExtendNotebookIdleCommand(G(r.WorkspaceId, "workspace_id"), G(r.ServerId, "server_id")), ctx.CancellationToken));

    public override async Task<NotebookServer> StartServer(StartNotebookRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new StartNotebookCommand(G(r.WorkspaceId, "workspace_id"), G(r.ServerId, "server_id"),
            string.IsNullOrEmpty(r.HostId) ? null : G(r.HostId, "host_id")), ctx.CancellationToken));

    public override async Task<MLPlatform.Contracts.V1.Empty> DeleteServer(NotebookRef r, ServerCallContext ctx)
    {
        await mediator.Send(new DeleteNotebookCommand(G(r.WorkspaceId, "workspace_id"), G(r.ServerId, "server_id")), ctx.CancellationToken);
        return new MLPlatform.Contracts.V1.Empty();
    }

    private static NotebookServer ToProto(ServerRow s) => new()
    {
        Id = s.Id.ToString(), Name = s.Name, Owner = s.Username, Profile = s.Profile, Image = s.Image, Ide = s.Ide, Cpus = s.Cpus,
        MemoryGb = s.MemoryGb, Gpus = s.Gpus, HostId = s.HostId?.ToString() ?? "", JobId = s.JobId?.ToString() ?? "", State = s.State,
        StateReason = s.StateReason ?? "", IdleTimeoutMin = s.IdleTimeoutMin, LastActivityAt = Ts(s.LastActivityAt), CreatedAt = Ts(s.CreatedAt),
        StartedAt = Ts(s.StartedAt), StoppedAt = Ts(s.StoppedAt), Url = NotebookRules.BaseUrl(s.Id) + "lab", PublicUrl = PublicRoutes.NotebookUrl(s.Id, ""),
    };
}
