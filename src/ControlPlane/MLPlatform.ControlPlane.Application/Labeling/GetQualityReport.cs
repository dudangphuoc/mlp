using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Annotators who are only annotators see their own row; Leads, Reviewers and labeling managers see
/// everyone.</summary>
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
