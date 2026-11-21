using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

/// <summary>Restarts a stopped/failed notebook: a new job with the same config (the home volume is kept, so files
/// remain), on the previous machine by default.</summary>
public sealed record StartNotebookCommand(Guid WorkspaceId, Guid ServerId, Guid? HostId) : ICommand<ServerRow>;

public sealed class StartNotebookCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, TokenService tokens, IMediator mediator)
    : ICommandHandler<StartNotebookCommand, ServerRow>
{
    public async ValueTask<ServerRow> Handle(StartNotebookCommand c, CancellationToken ct)
    {
        var s = await NotebookAccess.LoadAsync(db, auth, c.WorkspaceId, c.ServerId, ct);
        if (s.State is not ("stopped" or "failed"))
            throw new PreconditionException("NB_NOT_STOPPED", "Notebook is running or starting");
        if (await db.CountActiveServersAsync(c.WorkspaceId, s.UserId, ct) >= NotebookRules.MaxActivePerUser)
            throw new ResourceExhaustedException("NB_QUOTA_EXCEEDED", $"Each user can have at most {NotebookRules.MaxActivePerUser} running notebooks");
        if ((c.HostId ?? s.HostId) is not { } hostId) throw new ValidationException(new Dictionary<string, string> { ["host_id"] = "Select a machine to run the notebook" });
        var host = await mediator.Query(new MLPlatform.ControlPlane.Application.Fleet.GetHostQuery(c.WorkspaceId, hostId), ct);
        if (host.State is not ("online" or "busy")) throw new PreconditionException("NB_HOST_OFFLINE", $"Machine {host.Name} is {host.State} — choose an online machine");
        var image = s.Image;
        if (s.Image.StartsWith("mlp/nb-", StringComparison.Ordinal) && await db.GetProfileAsync(s.Profile, ct) is { } profile)
            image = s.Gpus > 0 ? profile.ImageGpu : profile.ImageCpu;
        var spec = new JobSpecDto
        {
            Image = image, Command = NotebookRules.Command(s.Ide, s.Id, NotebookRules.JupyterToken(tokens, s.Id)),
            Cpus = s.Cpus, Gpus = s.Gpus, MemoryBytes = s.MemoryGb * 1024L * 1024 * 1024, TimeoutSeconds = int.MaxValue, Hosts = [hostId],
            Env = new Dictionary<string, string> { ["MLP_NOTEBOOK"] = s.Id.ToString(), ["MLP_HOME_VOLUME"] = $"mlp-home-{c.WorkspaceId:N}-{s.UserId:N}" },
        };
        var jobId = await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "notebook", "nb-" + s.Name, spec, 60), ct);
        await db.RestartServerAsync(s.Id, c.WorkspaceId, jobId, image, hostId, clock.UtcNow, ct);
        return (await db.GetServerAsync(s.Id, c.WorkspaceId, ct))!;
    }
}
