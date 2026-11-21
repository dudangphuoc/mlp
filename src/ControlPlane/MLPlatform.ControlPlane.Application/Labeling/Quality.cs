using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/*
 * Hermes phase 4: annotator quality and calibration. Everything is computed from what already happened — submits, reviewer
 * decisions, Hermes checks — so each number can be traced back to items and checks.
 */

public sealed record QualityRow(Guid AnnotatorId, string? AnnotatorName, long Items, long Submitted, long Accepted, long Rejected, long InReview,
    long ReturnedEver, long Checked, long HermesPass, long HermesFlag, long HermesFail, long AutoAccepted, long Undone, double? AvgConfidence);

public sealed record CalibrationRow(string Verdict, double? Confidence, string Action, string Outcome);

public static partial class QualitySql
{
    [SqlQuery("Label/QualityByAnnotator.sql")]
    public static partial Task<List<QualityRow>> QualityByAnnotatorAsync(this IReadSession db, Guid projectId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/CalibrationRows.sql")]
    public static partial Task<List<CalibrationRow>> CalibrationRowsAsync(this IReadSession db, Guid projectId, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/SetHermesExplanation.sql")]
    public static partial Task SetHermesExplanationAsync(this IWriteSession db, Guid id, Guid workspaceId, string explanation, string lang, Guid? runId,
        DateTimeOffset now, CancellationToken ct);
}

public sealed record LabelQualityReport(ProjectRow Project, List<QualityRow> Annotators, List<CalibrationRow> Calibration, bool CanManage, Guid ViewerId);

/// <summary>Annotators who are only annotators see their own row; Leads, Reviewers and labeling managers see everyone.</summary>
public sealed record GetQualityReportQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<LabelQualityReport>;
public sealed class GetQualityReportQueryHandler(IReadSession db, LabelAccess access, WorkspaceAuthorizer auth) : IQueryHandler<GetQualityReportQuery, LabelQualityReport>
{
    public async ValueTask<LabelQualityReport> Handle(GetQualityReportQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        var manage = roles.Contains("lead") || await auth.HasAsync(q.WorkspaceId, "labeling.manage", ct);
        var onlySelf = roles.Count > 0 && roles.All(r => r == "annotator") && !manage;
        var rows = await db.QualityByAnnotatorAsync(p.Id, p.WorkspaceId, ct);
        if (onlySelf) rows = [.. rows.Where(r => r.AnnotatorId == uid)];
        return new LabelQualityReport(p, rows, await db.CalibrationRowsAsync(p.Id, p.WorkspaceId, ct), manage, uid);
    }
}

/// <summary>Stores the AI explanation of a check (internal: the api authorized the caller through GetHermesCheckQuery first).</summary>
public sealed record SetHermesExplanationCommand(Guid WorkspaceId, Guid CheckId, string Explanation, string Lang, Guid? RunId) : ICommand<bool>, IInternalCommand;
public sealed class SetHermesExplanationCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SetHermesExplanationCommand, bool>
{
    public async ValueTask<bool> Handle(SetHermesExplanationCommand c, CancellationToken ct)
    {
        await db.SetHermesExplanationAsync(c.CheckId, c.WorkspaceId, c.Explanation, c.Lang, c.RunId, clock.UtcNow, ct);
        return true;
    }
}
