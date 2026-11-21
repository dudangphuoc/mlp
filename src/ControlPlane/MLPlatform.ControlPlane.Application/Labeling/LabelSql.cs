using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record SchemaRow(Guid Id, Guid DatasetId, int Version, string Task, string Classes, DateTimeOffset CreatedAt);
public sealed record ProjectRow(Guid Id, Guid WorkspaceId, Guid DatasetId, string Name, string GuidelineMd, int SchemaVersion,
    string ReviewMode, int LockTtlMin, string State, DateTimeOffset CreatedAt, Guid? VersionId, double AutoAcceptThreshold, bool HermesEnabled);
public sealed record ProjectMemberRow(Guid UserId, string Username, string Role, int MaxOpenItems, long OpenItems);
public sealed record RoleRow(string Role);
public sealed record PathRow(string Path);
public sealed record ItemStateRow(Guid ProjectId, Guid DatasetId, string Path, string Status, Guid? AnnotatorId, Guid? ReviewerId,
    Guid? LockOwner, DateTimeOffset? LockExpiresAt, Guid? DraftBy, string? DraftAnnotations, DateTimeOffset? DraftSavedAt,
    string? SkipReason, int Revision);
public sealed record AnnotationRow(Guid Id, string Kind, int ClassId, string Geometry, string Attributes, string Source, double? Confidence);
public sealed record HistoryRow(string Action, string Username, DateTimeOffset At);
public sealed record CommentRow(Guid Id, string Author, string Body, bool Resolved, DateTimeOffset CreatedAt);
public sealed record StatRow(string Status, long N);
public sealed record MyItemRow(string Path, string Status, int Revision);
public sealed record LabelSetSourceRow(string Path, string Kind, int ClassId, string Geometry, string Attributes, string Source);
/// <summary>Progress of one project and the caller's own share of it.</summary>
public sealed record ProjectSummaryRow(Guid ProjectId, long Total, long Accepted, long InReview, long Unlabeled, long MyOpen, long MyReturned, string MyRoles);
/// <summary>One row of the project's item list.</summary>
public sealed record ItemListRow(string Path, string Status, Guid? AnnotatorId, Guid? ReviewerId, DateTimeOffset? SubmittedAt, DateTimeOffset? ReviewedAt,
    string? SkipReason, long Labels);
/// <summary>What one annotator has delivered in a project.</summary>
public sealed record MemberProgressRow(Guid UserId, long Submitted, long Accepted, long Returned, DateTimeOffset? LastAt);
/// <summary>Path of an item whose labels go into an exported version.</summary>
public sealed record ExportItemRow(string Path);
public sealed record LabelSetRow(Guid Id, int Number, int SchemaVersion, long ItemCount, long AnnotationCount, string IncludeStatus, DateTimeOffset CreatedAt);

public static partial class LabelSql
{
    [SqlQuery("Label/InsertSchema.sql")]
    public static partial Task InsertSchemaAsync(this IWriteSession db, Guid id, Guid datasetId, string task, string classes, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/GetCurrentSchema.sql")]
    public static partial Task<SchemaRow?> GetCurrentSchemaAsync(this IReadSession db, Guid datasetId, CancellationToken ct);
    [SqlQuery("Label/GetSchemaVersion.sql")]
    public static partial Task<SchemaRow?> GetSchemaVersionAsync(this IReadSession db, Guid datasetId, int version, CancellationToken ct);
    [SqlQuery("Label/InsertProject.sql")]
    public static partial Task InsertProjectAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid datasetId, string name, string guidelineMd,
        int schemaVersion, string reviewMode, int lockTtlMin, Guid createdBy, DateTimeOffset now, Guid? versionId, double autoAcceptThreshold, CancellationToken ct);
    [SqlQuery("Label/GetProject.sql")]
    public static partial Task<ProjectRow?> GetProjectAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Label/ListProjects.sql")]
    public static partial Task<List<ProjectRow>> ListProjectsAsync(this IReadSession db, Guid workspaceId, Guid? datasetId, CancellationToken ct);
    [SqlQuery("Label/SetProjectState.sql")]
    public static partial Task SetProjectStateAsync(this IWriteSession db, Guid id, string state, CancellationToken ct);
    [SqlQuery("Label/SeedItems.sql")]
    public static partial Task<long> SeedItemsAsync(this IWriteSession db, Guid projectId, Guid datasetId, bool imagesOnly, string[] exts, CancellationToken ct);
    [SqlQuery("Label/SeedItemsFromVersion.sql")]
    public static partial Task<long> SeedItemsFromVersionAsync(this IWriteSession db, Guid projectId, Guid datasetId, Guid versionId, bool imagesOnly, string[] exts, CancellationToken ct);
    [SqlQuery("Label/ClaimItem.sql")]
    public static partial Task<PathRow?> ClaimItemAsync(this IWriteSession db, Guid projectId, string path, Guid userId, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken ct);
    [SqlQuery("Label/SetProjectVersion.sql")]
    public static partial Task SetProjectVersionAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid versionId, CancellationToken ct);
    [SqlQuery("Label/LabelsImported.sql")]
    public static partial Task<bool?> LabelsImportedAsync(this IReadSession db, Guid projectId, string path, CancellationToken ct);
    [SqlQuery("Label/MarkLabelsImported.sql")]
    public static partial Task MarkLabelsImportedAsync(this IWriteSession db, Guid projectId, string path, CancellationToken ct);
    [SqlQuery("Label/GetVersionFile.sql")]
    public static partial Task<MLPlatform.ControlPlane.Application.Data.VersionFileRow?> GetVersionFileAsync(this IReadSession db, Guid versionId, string path, CancellationToken ct);
    [SqlQuery("Label/UpsertMember.sql")]
    public static partial Task UpsertMemberAsync(this IWriteSession db, Guid projectId, Guid userId, string role, int maxOpenItems, CancellationToken ct);
    [SqlQuery("Label/RemoveMember.sql")]
    public static partial Task RemoveMemberAsync(this IWriteSession db, Guid projectId, Guid userId, string role, CancellationToken ct);
    [SqlQuery("Label/ListProjectMembers.sql")]
    public static partial Task<List<ProjectMemberRow>> ListProjectMembersAsync(this IReadSession db, Guid projectId, CancellationToken ct);
    [SqlQuery("Label/MemberRoles.sql")]
    public static partial Task<List<RoleRow>> MemberRolesAsync(this IReadSession db, Guid projectId, Guid userId, CancellationToken ct);
    [SqlQuery("Label/CountOpen.sql")]
    public static partial Task<long> CountOpenAsync(this IReadSession db, Guid projectId, Guid userId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/ClaimNextItem.sql")]
    public static partial Task<PathRow?> ClaimNextItemAsync(this IWriteSession db, Guid projectId, Guid userId, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken ct);
    [SqlQuery("Label/ClaimNextReview.sql")]
    public static partial Task<PathRow?> ClaimNextReviewAsync(this IWriteSession db, Guid projectId, Guid userId, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken ct);
    [SqlQuery("Label/GetItemState.sql")]
    public static partial Task<ItemStateRow?> GetItemStateAsync(this IReadSession db, Guid projectId, string path, CancellationToken ct);
    [SqlQuery("Label/LockItemState.sql")]
    public static partial Task<ItemStateRow?> LockItemStateAsync(this IWriteSession db, Guid projectId, string path, CancellationToken ct);
    [SqlQuery("Label/UpdateItemState.sql")]
    public static partial Task UpdateItemStateAsync(this IWriteSession db, Guid projectId, string path, string status, Guid? lockOwner,
        DateTimeOffset? lockExpiresAt, Guid? reviewerId, string? skipReason, bool markSubmitted, bool markReviewed, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/BumpRevision.sql")]
    public static partial Task BumpRevisionAsync(this IWriteSession db, Guid projectId, string path, CancellationToken ct);
    [SqlQuery("Label/SaveDraft.sql")]
    public static partial Task SaveDraftAsync(this IWriteSession db, Guid projectId, string path, string draft, Guid userId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/ExtendLock.sql")]
    public static partial Task ExtendLockAsync(this IWriteSession db, Guid projectId, string path, Guid userId, DateTimeOffset expiresAt, CancellationToken ct);
    [SqlQuery("Label/ListAnnotations.sql")]
    public static partial Task<List<AnnotationRow>> ListAnnotationsAsync(this IReadSession db, Guid datasetId, string path, CancellationToken ct);
    [SqlQuery("Label/DeleteAnnotations.sql")]
    public static partial Task DeleteAnnotationsAsync(this IWriteSession db, Guid datasetId, string path, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/InsertAnnotation.sql")]
    public static partial Task InsertAnnotationAsync(this IWriteSession db, Guid id, Guid datasetId, string path, int schemaVersion, string kind, int classId,
        string geometry, string attributes, string source, double? confidence, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/InsertHistory.sql")]
    public static partial Task InsertHistoryAsync(this IWriteSession db, Guid id, Guid datasetId, string path, string action, string? before, string? after,
        Guid actorId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/ListHistory.sql")]
    public static partial Task<List<HistoryRow>> ListHistoryAsync(this IReadSession db, Guid datasetId, string path, CancellationToken ct);
    [SqlQuery("Label/InsertComment.sql")]
    public static partial Task InsertCommentAsync(this IWriteSession db, Guid id, Guid projectId, string path, Guid authorId, string body, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/ListComments.sql")]
    public static partial Task<List<CommentRow>> ListCommentsAsync(this IReadSession db, Guid projectId, string path, CancellationToken ct);
    [SqlQuery("Label/ProjectStats.sql")]
    public static partial Task<List<StatRow>> ProjectStatsAsync(this IReadSession db, Guid projectId, CancellationToken ct);
    [SqlQuery("Label/ListMyItems.sql")]
    public static partial Task<List<MyItemRow>> ListMyItemsAsync(this IReadSession db, Guid projectId, Guid userId, CancellationToken ct);
    [SqlQuery("Label/ProjectSummaries.sql")]
    public static partial Task<List<ProjectSummaryRow>> ProjectSummariesAsync(this IReadSession db, Guid workspaceId, Guid userId, CancellationToken ct);
    [SqlQuery("Label/ListItems.sql")]
    public static partial Task<List<ItemListRow>> ListItemsAsync(this IReadSession db, Guid projectId, string status, string search, bool mine, Guid userId,
        int take, int skip, CancellationToken ct);
    [SqlQuery("Label/CountItems.sql")]
    public static partial Task<long> CountItemsAsync(this IReadSession db, Guid projectId, string status, string search, bool mine, Guid userId, CancellationToken ct);
    [SqlQuery("Label/MemberProgress.sql")]
    public static partial Task<List<MemberProgressRow>> MemberProgressAsync(this IReadSession db, Guid projectId, CancellationToken ct);
    [SqlQuery("Label/ExportItems.sql")]
    public static partial Task<List<ExportItemRow>> ExportItemsAsync(this IReadSession db, Guid projectId, bool allLabeled, CancellationToken ct);
    [SqlQuery("Label/ExportAnnotations.sql")]
    public static partial Task<List<LabelSetSourceRow>> ExportAnnotationsAsync(this IReadSession db, Guid datasetId, Guid projectId, bool allLabeled, CancellationToken ct);
    [SqlQuery("Label/LabelSetRows.sql")]
    public static partial Task<List<LabelSetSourceRow>> LabelSetRowsAsync(this IReadSession db, Guid datasetId, bool allLabeled, CancellationToken ct);
    [SqlQuery("Label/InsertLabelSet.sql")]
    public static partial Task<int> InsertLabelSetAsync(this IWriteSession db, Guid id, Guid datasetId, int schemaVersion, string snapshotKey,
        long itemCount, long annotationCount, string includeStatus, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Label/ListLabelSets.sql")]
    public static partial Task<List<LabelSetRow>> ListLabelSetsAsync(this IReadSession db, Guid datasetId, CancellationToken ct);
}
