using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record RecordSuggestionSetCommand(Guid Id, Guid WorkspaceId, Guid ProjectId, string Path, int Revision, string SourceKind, Guid SourceId,
    string SourceLabel, bool External, string State, string Detail, string ItemsJson, string UnmappedJson, Guid? AiRunId, int LatencyMs, Guid? JobId, Guid CreatedBy)
    : ICommand<bool>, IInternalCommand;

public sealed class RecordSuggestionSetCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordSuggestionSetCommand, bool>
{
    public async ValueTask<bool> Handle(RecordSuggestionSetCommand c, CancellationToken ct)
    {
        await db.InsertSuggestionSetAsync(c.Id, c.WorkspaceId, c.ProjectId, c.Path, c.Revision, c.SourceKind, c.SourceId, c.SourceLabel, c.External, c.State,
            c.Detail, c.ItemsJson, c.UnmappedJson, c.AiRunId, c.LatencyMs, c.JobId, c.CreatedBy, clock.UtcNow, ct);
        return true;
    }
}
