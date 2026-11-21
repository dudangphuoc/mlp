using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

/// <summary>§5 step 1: server = `notebook` job via the scheduler (a holder reserves CPU/GPU, D-15) → the agent runs the
/// JupyterLab container.</summary>
[RequiresPermission("notebook.use")]
public sealed record CreateNotebookCommand(Guid WorkspaceId, string Name, string Profile, string Ide, double Cpus, int MemoryGb, int Gpus,
    int IdleTimeoutMin, string? ImageOverride, string? Network, Guid? HostId = null) : ICommand<ServerRow>;

public sealed class CreateNotebookCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, TokenService tokens, IMediator mediator)
    : ICommandHandler<CreateNotebookCommand, ServerRow>
{
    public async ValueTask<ServerRow> Handle(CreateNotebookCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "notebook.use", ct);
        var profile = await db.GetProfileAsync(string.IsNullOrEmpty(c.Profile) ? "base" : c.Profile, ct)
                      ?? throw new NotFoundException("NB_PROFILE_NOT_FOUND", "Profile not found");
        var ide = string.IsNullOrEmpty(c.Ide) ? "jupyterlab" : c.Ide;
        if (await db.CountActiveServersAsync(c.WorkspaceId, uid, ct) >= NotebookRules.MaxActivePerUser)
            throw new ResourceExhaustedException("NB_QUOTA_EXCEEDED", $"Each user can have at most {NotebookRules.MaxActivePerUser} running notebooks");
        if (c.HostId is not { } hostId) throw new ValidationException(new Dictionary<string, string> { ["host_id"] = "Select a machine to run the notebook" });
        var host = await mediator.Query(new MLPlatform.ControlPlane.Application.Fleet.GetHostQuery(c.WorkspaceId, hostId), ct);
        if (host.State is not ("online" or "busy")) throw new PreconditionException("NB_HOST_OFFLINE", $"Machine {host.Name} is {host.State} — choose an online machine");
        var id = Ids.New();
        var image = !string.IsNullOrEmpty(c.ImageOverride) ? c.ImageOverride : c.Gpus > 0 ? profile.ImageGpu : profile.ImageCpu;
        var spec = new JobSpecDto
        {
            Image = image, Command = NotebookRules.Command(ide, id, NotebookRules.JupyterToken(tokens, id)),
            Cpus = c.Cpus <= 0 ? 1 : c.Cpus, Gpus = Math.Max(0, c.Gpus), MemoryBytes = Math.Max(0, c.MemoryGb) * 1024L * 1024 * 1024,
            Network = string.IsNullOrEmpty(c.Network) ? null : c.Network, TimeoutSeconds = int.MaxValue, Hosts = [hostId],
            Env = new Dictionary<string, string> { ["MLP_NOTEBOOK"] = id.ToString(), ["MLP_HOME_VOLUME"] = $"mlp-home-{c.WorkspaceId:N}-{uid:N}" },
        };
        var name = string.IsNullOrWhiteSpace(c.Name) ? $"{profile.Name}-{id.ToString()[^6..]}" : c.Name.Trim();
        var jobId = await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "notebook", "nb-" + name, spec, 60), ct);
        await db.InsertServerAsync(id, c.WorkspaceId, uid, name, profile.Name, image, ide, spec.Cpus, Math.Max(0, c.MemoryGb), spec.Gpus, jobId,
            c.IdleTimeoutMin is >= 5 and <= 24 * 60 ? c.IdleTimeoutMin : 60, clock.UtcNow, ct);
        return (await db.GetServerAsync(id, c.WorkspaceId, ct))!;
    }
}
