using System.Globalization;
using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>What each annotator delivered (submitted, accepted, returned, last submit) — shown next to the member
/// list.</summary>
public sealed record ListLabelingMemberProgressQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<IReadOnlyList<MemberProgressRow>>;

/// <summary>Counts per annotator from the item states.</summary>
public sealed class ListLabelingMemberProgressQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<ListLabelingMemberProgressQuery, IReadOnlyList<MemberProgressRow>>
{
    public async ValueTask<IReadOnlyList<MemberProgressRow>> Handle(ListLabelingMemberProgressQuery q, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        return await db.MemberProgressAsync(p.Id, ct);
    }
}
