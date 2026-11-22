using Google.Protobuf;
using MLPlatform.ControlPlane.Application.Fleet.Ops;
using MLPlatform.Contracts.V1;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Maps an application remote ops request to the agent command and its time limit.</summary>
public static class HostOpsCommands
{
    public static (AgentCommand Command, TimeSpan Limit) Build(HostOpsRequest r) => r.Kind switch
    {
        HostOpsKind.FsList => (new AgentCommand { FsList = new FsList { Path = r.Path } }, TimeSpan.FromSeconds(20)),
        HostOpsKind.FsRead => (new AgentCommand { FsRead = new FsRead { Path = r.Path, Offset = r.Offset, Length = r.Length } }, TimeSpan.FromSeconds(60)),
        HostOpsKind.FsWrite => (new AgentCommand { FsWrite = new FsWrite { Path = r.Path, Offset = r.Offset, Data = ByteString.CopyFrom(r.Data.Span), Truncate = r.Truncate } },
            TimeSpan.FromSeconds(60)),
        HostOpsKind.FsMkdir => (new AgentCommand { FsMkdir = new FsMkdir { Path = r.Path } }, TimeSpan.FromSeconds(20)),
        HostOpsKind.FsMove => (new AgentCommand { FsMove = new FsMove { From = r.Path, To = r.To } }, TimeSpan.FromSeconds(60)),
        HostOpsKind.FsDelete => (new AgentCommand { FsDelete = new FsDelete { Path = r.Path, Recursive = r.Recursive } }, TimeSpan.FromMinutes(5)),
        HostOpsKind.ProcessList => (new AgentCommand { ProcessList = new ProcessList { Sort = r.Sort } }, TimeSpan.FromSeconds(20)),
        HostOpsKind.ProcessKill => (new AgentCommand { ProcessKill = new ProcessKill { Pid = r.Pid, Force = r.Force, Tree = r.Tree } }, TimeSpan.FromSeconds(20)),
        HostOpsKind.ServiceList => (new AgentCommand { ServiceList = new ServiceList() }, TimeSpan.FromSeconds(60)),
        HostOpsKind.ServiceControl => (new AgentCommand { ServiceControl = new ServiceControl { Name = r.Name, Action = r.Action } }, TimeSpan.FromSeconds(90)),
        HostOpsKind.SystemLog => (new AgentCommand { SystemLogRead = new SystemLogRead { Unit = r.Name, Lines = r.Lines } }, TimeSpan.FromSeconds(45)),
        _ => throw new ArgumentOutOfRangeException(nameof(r), r.Kind, null),
    };
}
