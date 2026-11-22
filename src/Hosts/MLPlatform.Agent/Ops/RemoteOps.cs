using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Ops;

/// <summary>Dispatches remote ops commands (files, processes, services, logs).</summary>
public sealed class RemoteOps(AgentState state)
{
    public static bool Handles(AgentCommand.KindOneofCase k) => k is >= AgentCommand.KindOneofCase.FsList and <= AgentCommand.KindOneofCase.SystemLogRead;

    public static bool IsQuiet(AgentCommand.KindOneofCase k) =>
        k is AgentCommand.KindOneofCase.FsList or AgentCommand.KindOneofCase.FsRead or AgentCommand.KindOneofCase.ProcessList
            or AgentCommand.KindOneofCase.ServiceList or AgentCommand.KindOneofCase.SystemLogRead;

    public async Task<CommandResult> ExecuteAsync(AgentCommand cmd, CancellationToken ct)
    {
        var fs = new HostFs(PathGuard.Roots(state.Options, state.Dir));
        try
        {
            return cmd.KindCase switch
            {
                AgentCommand.KindOneofCase.FsList => fs.List(cmd.FsList),
                AgentCommand.KindOneofCase.FsRead => await fs.ReadAsync(cmd.FsRead, ct),
                AgentCommand.KindOneofCase.FsWrite => await fs.WriteAsync(cmd.FsWrite, ct),
                AgentCommand.KindOneofCase.FsMkdir => fs.Mkdir(cmd.FsMkdir),
                AgentCommand.KindOneofCase.FsMove => fs.Move(cmd.FsMove),
                AgentCommand.KindOneofCase.FsDelete => fs.Delete(cmd.FsDelete),
                AgentCommand.KindOneofCase.ProcessList => await ProcessTable.ListAsync(cmd.ProcessList, ct),
                AgentCommand.KindOneofCase.ProcessKill => ProcessTable.Kill(cmd.ProcessKill),
                AgentCommand.KindOneofCase.ServiceList => await HostServices.ListAsync(ct),
                AgentCommand.KindOneofCase.ServiceControl => await HostServices.ControlAsync(cmd.ServiceControl, ct),
                AgentCommand.KindOneofCase.SystemLogRead => await SystemLog.ReadAsync(cmd.SystemLogRead, ct),
                _ => OpsResult.Fail("FLEET_COMMAND_UNSUPPORTED", cmd.KindCase.ToString()),
            };
        }
        catch (UnauthorizedAccessException e) { return OpsResult.Fail("FLEET_ACCESS_DENIED", e.Message); }
        catch (IOException e) { return OpsResult.Fail("FLEET_IO_FAILED", e.Message); }
    }
}
