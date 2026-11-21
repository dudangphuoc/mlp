using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

public sealed record ProfileRow(string Name, string DisplayName, string Description, string ImageCpu, string ImageGpu, string Version);
public sealed record StoppingServerRow(Guid Id, Guid? JobId, string JobState, DateTimeOffset StateAt, string? StateReason);
public sealed record ServerRow(Guid Id, Guid WorkspaceId, Guid UserId, string Username, string Name, string Profile, string Image, string Ide,
    double Cpus, int MemoryGb, int Gpus, Guid? HostId, string? Endpoint, Guid? JobId, string State, string? StateReason, int IdleTimeoutMin,
    DateTimeOffset LastActivityAt, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? StoppedAt);

public sealed record IdleWarnRow(Guid Id, Guid WorkspaceId, Guid UserId, string Name);

public static partial class NbSql
{
    [SqlQuery("Nb/ListProfiles.sql")]
    public static partial Task<List<ProfileRow>> ListProfilesAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Nb/GetProfile.sql")]
    public static partial Task<ProfileRow?> GetProfileAsync(this IReadSession db, string name, CancellationToken ct);
    [SqlQuery("Nb/RestartServer.sql")]
    public static partial Task RestartServerAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid jobId, string image, Guid? hostId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Nb/DeleteServer.sql")]
    public static partial Task DeleteServerAsync(this IWriteSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Nb/InsertServer.sql")]
    public static partial Task InsertServerAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid userId, string name, string profile, string image,
        string ide, double cpus, int memoryGb, int gpus, Guid jobId, int idleTimeoutMin, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Nb/GetServer.sql")]
    public static partial Task<ServerRow?> GetServerAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Nb/GetServerById.sql")]
    public static partial Task<ServerRow?> GetServerByIdAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Nb/GetServerByJob.sql")]
    public static partial Task<ServerRow?> GetServerByJobAsync(this IReadSession db, Guid jobId, CancellationToken ct);
    [SqlQuery("Nb/ListServers.sql")]
    public static partial Task<List<ServerRow>> ListServersAsync(this IReadSession db, Guid workspaceId, Guid userId, bool all, CancellationToken ct);
    [SqlQuery("Nb/ListIdleServers.sql")]
    public static partial Task<List<ServerRow>> ListIdleServersAsync(this IReadSession db, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Nb/CountActiveServers.sql")]
    public static partial Task<long> CountActiveServersAsync(this IReadSession db, Guid workspaceId, Guid userId, CancellationToken ct);
    [SqlQuery("Nb/SetServerState.sql")]
    public static partial Task SetServerStateAsync(this IWriteSession db, Guid id, string state, string? reason, Guid? hostId, string? endpoint,
        DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Nb/ListStoppingServers.sql")]
    public static partial Task<List<StoppingServerRow>> ListStoppingServersAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Nb/ClaimIdleWarnings.sql")]
    public static partial Task<List<IdleWarnRow>> ClaimIdleWarningsAsync(this IWriteSession db, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Nb/TouchServer.sql")]
    public static partial Task TouchServerAsync(this IWriteSession db, Guid id, DateTimeOffset now, CancellationToken ct);
}

public static class NotebookRules
{
    public const int ContainerPort = 8888;
    public const string HomeMount = "/home/mlp";
    public const int MaxActivePerUser = 3;   // temporary until sched.quotas exists (R7)

    /// <summary>Jupyter token derived from the server key — not stored in the DB; the api attaches it when proxying (§5 step 2).</summary>
    public static string JupyterToken(TokenService tokens, Guid serverId) => tokens.Hash("nb-jupyter:" + serverId);

    public static string BaseUrl(Guid serverId) => $"/nb/{serverId}/";

    /// <summary>JupyterLab listens only inside the container; the host port is bound to 127.0.0.1 only (R2).</summary>
    public static List<string> Command(string ide, Guid serverId, string token) => ide switch
    {
        "jupyterlab" =>
        [
            "jupyter", "lab", "--ip=0.0.0.0", $"--port={ContainerPort}", "--no-browser", $"--ServerApp.base_url={BaseUrl(serverId)}",
            $"--IdentityProvider.token={token}", "--ServerApp.allow_remote_access=True", "--ServerApp.allow_origin=*", $"--ServerApp.root_dir={HomeMount}",
        ],
        _ => throw new InvalidArgumentException("NB_IDE_UNSUPPORTED", "IDE not supported yet (available: jupyterlab)"),
    };
}

public sealed record ListProfilesQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ProfileRow>>;

public sealed class ListProfilesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListProfilesQuery, IReadOnlyList<ProfileRow>>
{
    public async ValueTask<IReadOnlyList<ProfileRow>> Handle(ListProfilesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListProfilesAsync(ct);
    }
}

/// <summary>§5 step 1: server = `notebook` job via the scheduler (a holder reserves CPU/GPU, D-15) → the agent runs the JupyterLab container.</summary>
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
        // The notebook runs on the machine the user picked (Fleet) — it must belong to the workspace and be online.
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

public sealed record ListNotebooksQuery(Guid WorkspaceId, bool All) : IQuery<IReadOnlyList<ServerRow>>;

public sealed class ListNotebooksQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListNotebooksQuery, IReadOnlyList<ServerRow>>
{
    public async ValueTask<IReadOnlyList<ServerRow>> Handle(ListNotebooksQuery q, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(q.WorkspaceId, "notebook.use", ct);
        var all = q.All && await auth.HasAsync(q.WorkspaceId, "notebook.admin", ct);
        return await db.ListServersAsync(q.WorkspaceId, uid, all, ct);
    }
}

/// <summary>Owner or Admin; anyone else → NOT_FOUND (existence is not revealed).</summary>
public sealed record GetNotebookQuery(Guid WorkspaceId, Guid ServerId) : IQuery<ServerRow>;

public sealed class GetNotebookQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetNotebookQuery, ServerRow>
{
    public async ValueTask<ServerRow> Handle(GetNotebookQuery q, CancellationToken ct) => await NotebookAccess.LoadAsync(db, auth, q.WorkspaceId, q.ServerId, ct);
}

internal static class NotebookAccess
{
    public static async Task<ServerRow> LoadAsync(IReadSession db, WorkspaceAuthorizer auth, Guid ws, Guid id, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(ws, "notebook.use", ct);
        var s = await db.GetServerAsync(id, ws, ct);
        if (s is null || (s.UserId != uid && !await auth.HasAsync(ws, "notebook.admin", ct)))
            throw new NotFoundException("NB_NOT_FOUND", "Notebook not found");
        return s;
    }
}

/// <summary>
/// Marks it stopping and returns the job so the host stops the container + Ray job (WorkloadLauncher.StopJobAsync).
/// Stop on a notebook that is already stopping = force stop: it is marked stopped right away (the stop is sent to the machine again),
/// so a machine that never answers can no longer leave the user stuck.
/// </summary>
public sealed record StopNotebookCommand(Guid WorkspaceId, Guid ServerId) : ICommand<ServerRow>;

public sealed class StopNotebookCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<StopNotebookCommand, ServerRow>
{
    public async ValueTask<ServerRow> Handle(StopNotebookCommand c, CancellationToken ct)
    {
        var s = await NotebookAccess.LoadAsync(db, auth, c.WorkspaceId, c.ServerId, ct);
        if (s.State is "stopped" or "failed") return s;
        if (s.State == "stopping")
        {
            await db.SetServerStateAsync(s.Id, "stopped", "Force-stopped by user: the machine had not confirmed the stop; the stop was sent again", null, null, clock.UtcNow, ct);
            return (await db.GetServerAsync(s.Id, c.WorkspaceId, ct))!;
        }
        await db.SetServerStateAsync(s.Id, "stopping", "Stopped by user", null, null, clock.UtcNow, ct);
        return s with { State = "stopping" };
    }
}

/// <summary>
/// Restarts a stopped/failed notebook: a new job with the same config (the home volume is kept, so files remain), on the previous machine by default.
/// The image follows the current profile (picking up new images); an overridden image (dev) is kept.
/// </summary>
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

/// <summary>Removes a stopped/failed notebook from the list (the user's home volume on the machine is kept — it is shared with other notebooks).</summary>
public sealed record DeleteNotebookCommand(Guid WorkspaceId, Guid ServerId) : ICommand<bool>;

public sealed class DeleteNotebookCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<DeleteNotebookCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteNotebookCommand c, CancellationToken ct)
    {
        var s = await NotebookAccess.LoadAsync(db, auth, c.WorkspaceId, c.ServerId, ct);
        if (s.State is not ("stopped" or "failed")) throw new PreconditionException("NB_NOT_STOPPED", "Stop the notebook before deleting it");
        await db.DeleteServerAsync(s.Id, c.WorkspaceId, ct);
        return true;
    }
}

/// <summary>"Keep 1 more hour": pushes the activity timestamp forward so it is not auto-stopped (R4).</summary>
public sealed record ExtendNotebookIdleCommand(Guid WorkspaceId, Guid ServerId) : ICommand<ServerRow>;

public sealed class ExtendNotebookIdleCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<ExtendNotebookIdleCommand, ServerRow>
{
    public async ValueTask<ServerRow> Handle(ExtendNotebookIdleCommand c, CancellationToken ct)
    {
        var s = await NotebookAccess.LoadAsync(db, auth, c.WorkspaceId, c.ServerId, ct);
        if (s.State != "running") throw new PreconditionException("NB_NOT_RUNNING", "Notebook is not running");
        await db.TouchServerAsync(s.Id, clock.UtcNow.AddMinutes(60), ct);
        return (await db.GetServerAsync(s.Id, c.WorkspaceId, ct))!;
    }
}

// ───────────── Internal use (scheduler, proxy, reaper) — not called from RPC ─────────────

public sealed record SetNotebookStateByJobCommand(Guid JobId, string State, string? Reason, Guid? HostId, string? Endpoint) : ICommand<bool>;

public sealed class SetNotebookStateByJobCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SetNotebookStateByJobCommand, bool>
{
    public async ValueTask<bool> Handle(SetNotebookStateByJobCommand c, CancellationToken ct)
    {
        if (await db.GetServerByJobAsync(c.JobId, ct) is not { } s) return false;
        // Stopped deliberately → the container exiting means "stopped", not a failure.
        var state = s.State is "stopping" or "stopped" && c.State is "failed" or "stopped" ? "stopped" : c.State;
        if (s.State == "stopped" && state != "stopped") return true;
        await db.SetServerStateAsync(s.Id, state, state == "stopped" && s.State == "stopping" ? s.StateReason : c.Reason, c.HostId, c.Endpoint, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>Proxy `/nb/{id}/…`: notebook owner only; returns null → 404.</summary>
public sealed record ResolveNotebookProxyQuery(Guid ServerId, Guid UserId) : IQuery<ServerRow?>;

public sealed class ResolveNotebookProxyQueryHandler(IReadSession db) : IQueryHandler<ResolveNotebookProxyQuery, ServerRow?>
{
    public async ValueTask<ServerRow?> Handle(ResolveNotebookProxyQuery q, CancellationToken ct)
    {
        var s = await db.GetServerByIdAsync(q.ServerId, ct);
        return s is not null && s.UserId == q.UserId && s.State == "running" && s.HostId is not null && s.Endpoint is not null ? s : null;
    }
}

public sealed record TouchNotebookCommand(Guid ServerId) : ICommand<bool>;

public sealed class TouchNotebookCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<TouchNotebookCommand, bool>
{
    public async ValueTask<bool> Handle(TouchNotebookCommand c, CancellationToken ct)
    {
        await db.TouchServerAsync(c.ServerId, clock.UtcNow, ct);
        return true;
    }
}

public sealed record ListIdleNotebooksQuery : IQuery<IReadOnlyList<ServerRow>>;

public sealed class ListIdleNotebooksQueryHandler(IReadSession db, IClock clock) : IQueryHandler<ListIdleNotebooksQuery, IReadOnlyList<ServerRow>>
{
    public async ValueTask<IReadOnlyList<ServerRow>> Handle(ListIdleNotebooksQuery q, CancellationToken ct) => await db.ListIdleServersAsync(clock.UtcNow, ct);
}

/// <summary>
/// Reaper: finishes "stopping" notebooks. Job already over (or gone) → stopped. Still not confirmed after 5 minutes → stopped with
/// a reason saying so; the returned jobs get the stop sent again.
/// </summary>
public sealed record ReconcileStoppingNotebooksCommand : ICommand<List<Guid>>, IInternalCommand;

public sealed class ReconcileStoppingNotebooksCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<ReconcileStoppingNotebooksCommand, List<Guid>>
{
    public async ValueTask<List<Guid>> Handle(ReconcileStoppingNotebooksCommand c, CancellationToken ct)
    {
        var resend = new List<Guid>();
        foreach (var s in await db.ListStoppingServersAsync(ct))
        {
            if (s.JobId is null || s.JobState is "" or "succeeded" or "failed" or "cancelled")
                await db.SetServerStateAsync(s.Id, "stopped", s.StateReason ?? "Stopped", null, null, clock.UtcNow, ct);
            else if (clock.UtcNow - s.StateAt > TimeSpan.FromMinutes(5))
            {
                await db.SetServerStateAsync(s.Id, "stopped", "The machine did not confirm the stop within 5 minutes; marked stopped and the stop was sent again", null, null, clock.UtcNow, ct);
                resend.Add(s.JobId.Value);
            }
        }
        return resend;
    }
}

public sealed record MarkNotebookStoppingCommand(Guid ServerId, string Reason) : ICommand<bool>;

public sealed class MarkNotebookStoppingCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<MarkNotebookStoppingCommand, bool>
{
    public async ValueTask<bool> Handle(MarkNotebookStoppingCommand c, CancellationToken ct)
    {
        await db.SetServerStateAsync(c.ServerId, "stopping", c.Reason, null, null, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>R4: warns 10 minutes before a notebook is auto-stopped (M16 `notebook.idle_warning`). Called by the reaper every minute.</summary>
public sealed record WarnIdleNotebooksCommand : ICommand<int>;

public sealed class WarnIdleNotebooksCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<WarnIdleNotebooksCommand, int>
{
    public async ValueTask<int> Handle(WarnIdleNotebooksCommand c, CancellationToken ct)
    {
        var rows = await db.ClaimIdleWarningsAsync(clock.UtcNow, ct);
        foreach (var r in rows)
            await MLPlatform.ControlPlane.Application.Notifications.Notifier.SendAsync(db, clock, r.WorkspaceId, [r.UserId], "notebook.idle_warning",
                $"Notebook \"{r.Name}\" will stop automatically in 10 minutes if unused", $"#/notebooks/{r.Id}", $"notebook/{r.Id}", "warning", ct);
        return rows.Count;
    }
}
