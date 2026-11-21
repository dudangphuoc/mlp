using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Stores a finished check (internal: called by the api checker after the caller was authorized by GetHermesContextQuery).</summary>
public sealed record RecordHermesCheckCommand(Guid Id, Guid WorkspaceId, Guid ProjectId, string Path, int Revision, string Trigger, string Verdict,
    double? Confidence, string DecidedBy, string Summary, string StepsJson, string? Suggestion, Guid? AiRunId, string Action, Guid? ActorId)
    : ICommand<bool>, IInternalCommand;

public sealed class RecordHermesCheckCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordHermesCheckCommand, bool>
{
    public async ValueTask<bool> Handle(RecordHermesCheckCommand c, CancellationToken ct)
    {
        await db.InsertHermesCheckAsync(c.Id, c.WorkspaceId, c.ProjectId, c.Path, c.Revision, c.Trigger, c.Verdict, c.Confidence, c.DecidedBy, c.Summary,
            c.StepsJson, c.Suggestion, c.AiRunId, c.Action, c.ActorId, clock.UtcNow, ct);
        return true;
    }
}
