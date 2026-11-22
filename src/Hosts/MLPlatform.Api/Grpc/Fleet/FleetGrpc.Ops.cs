using Grpc.Core;
using MLPlatform.ControlPlane.Application.Fleet.Ops;
using MLPlatform.Contracts.V1;
using Empty = MLPlatform.Contracts.V1.Empty;

namespace MLPlatform.Api.Grpc;

/// <summary>Remote ops RPCs (docs/agent-upgrade.md §1): files, processes, services, system log.</summary>
public sealed partial class FleetGrpc
{
    private static CommandResult Result(HostOpsReply r) => CommandResult.Parser.ParseFrom(r.Result);

    public override async Task<FsListing> ListHostFiles(HostPathRequest r, ServerCallContext ctx) =>
        Result(await mediator.Query(new ListHostFilesQuery(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id"), r.Path), ctx.CancellationToken)).FsListing;

    public override async Task<HostFileContent> ReadHostFile(ReadHostFileRequest r, ServerCallContext ctx)
    {
        var res = Result(await mediator.Query(new ReadHostFileQuery(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id"), r.Path, r.Offset, r.Length),
            ctx.CancellationToken));
        return new HostFileContent { Data = res.Data, Stat = res.FsStat };
    }

    public override async Task<FsStat> WriteHostFile(WriteHostFileRequest r, ServerCallContext ctx) =>
        Result(await mediator.Send(new WriteHostFileCommand(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id"), r.Path, r.Offset, r.Data.Memory, r.Truncate),
            ctx.CancellationToken)).FsStat;

    public override async Task<Empty> MakeHostDir(HostPathRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new MakeHostDirCommand(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id"), r.Path), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<Empty> MoveHostPath(MoveHostPathRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new MoveHostPathCommand(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id"), r.From, r.To), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<Empty> DeleteHostPath(DeleteHostPathRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new DeleteHostPathCommand(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id"), r.Path, r.Recursive), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<ProcessListing> ListHostProcesses(ListHostProcessesRequest r, ServerCallContext ctx) =>
        Result(await mediator.Query(new ListHostProcessesQuery(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id"), r.Sort), ctx.CancellationToken)).ProcessListing;

    public override async Task<Empty> KillHostProcess(KillHostProcessRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new KillHostProcessCommand(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id"), r.Pid, r.Force, r.Tree), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<ServiceListing> ListHostServices(GetHostRequest r, ServerCallContext ctx) =>
        Result(await mediator.Query(new ListHostServicesQuery(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id")), ctx.CancellationToken)).ServiceListing;

    public override async Task<Empty> ControlHostService(ControlHostServiceRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new ControlHostServiceCommand(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id"), r.Name, r.Action), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<HostSystemLog> ReadHostSystemLog(ReadHostSystemLogRequest r, ServerCallContext ctx)
    {
        var res = Result(await mediator.Query(new ReadHostSystemLogQuery(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id"), r.Unit, r.Lines), ctx.CancellationToken));
        var log = new HostSystemLog();
        log.Lines.AddRange(res.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        return log;
    }
}
