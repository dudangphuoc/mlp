using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>The annotator applied (labels copied into the editor, saved with the item) or dismissed a suggestion set.</summary>
public sealed record DecideSuggestionSetCommand(Guid WorkspaceId, Guid ProjectId, Guid Id, string Decision) : ICommand<SuggestionSetRow>;

public sealed class DecideSuggestionSetCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<DecideSuggestionSetCommand, SuggestionSetRow>
{
    public async ValueTask<SuggestionSetRow> Handle(DecideSuggestionSetCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.annotate", ct);
        if (c.Decision is not ("applied" or "dismissed")) throw LabelRules.Invalid("decision", "Decision must be applied or dismissed");
        var s = await db.GetSuggestionSetAsync(c.Id, p.WorkspaceId, ct);
        if (s is null || s.ProjectId != p.Id) throw new NotFoundException("LABEL_SUGGESTION_NOT_FOUND", "Suggestion not found");
        await db.DecideSuggestionSetAsync(s.Id, p.WorkspaceId, c.Decision, uid, clock.UtcNow, ct);
        return (await db.GetSuggestionSetAsync(s.Id, p.WorkspaceId, ct))!;
    }
}
