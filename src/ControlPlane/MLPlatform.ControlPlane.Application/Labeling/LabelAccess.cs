using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Per-project permissions (M05 §2.1): project_members is the single source of labeling roles.</summary>
public sealed class LabelAccess(IReadSession readDb, WorkspaceAuthorizer auth, IClock clock)
{
    private readonly IReadSession db = readDb;

    public async Task<(Guid Uid, ProjectRow Project, HashSet<string> Roles)> LoadAsync(Guid ws, Guid projectId, string permission, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(ws, permission, ct);
        var p = await db.GetLabelProjectAsync(projectId, ws, ct) ?? throw new NotFoundException("LABEL_PROJECT_NOT_FOUND", "Labeling project not found");
        var roles = (await db.MemberRolesAsync(projectId, uid, ct)).Select(r => r.Role).ToHashSet();
        // Whoever manages labeling in the workspace (owner, admin) leads every project: a project never ends up with nobody able to run it.
        if (await auth.HasAsync(ws, "labeling.manage", ct)) roles.Add("lead");
        return (uid, p, roles);
    }

    public static void RequireActive(ProjectRow p)
    {
        if (p.State != "active") throw new PreconditionException("LABEL_PROJECT_NOT_ACTIVE", "Project is not active / is paused");
    }

    /// <summary>The caller holds an unexpired lock — required for Save/Submit/Skip.</summary>
    public void RequireLock(ItemStateRow s, Guid uid)
    {
        if (s.LockOwner != uid)
            throw s.LockOwner is null || s.LockExpiresAt < clock.UtcNow
                ? new PreconditionException("LABEL_LOCK_EXPIRED", "Item lock has expired")
                : new PreconditionException("LABEL_ITEM_LOCKED", "Item is being labeled by someone else");
        if (s.LockExpiresAt < clock.UtcNow) throw new PreconditionException("LABEL_LOCK_EXPIRED", "Item lock has expired");
    }

    /// <summary>Reads an item with its labels. Inside a command, pass the write session so the lock just taken in the same transaction is visible.</summary>
    public async Task<ItemView> ViewAsync(ProjectRow p, string path, S3Client s3, CancellationToken ct, IReadSession? session = null)
    {
        var db = session ?? this.db;
        var s = await db.GetItemStateAsync(p.Id, path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        var schema = await db.GetSchemaVersionAsync(p.DatasetId, p.SchemaVersion, ct);
        string url;
        if (p.VersionId is { } vid)
        {
            // Version project: reference files come from their source, copied files from the version folder in storage.
            var v = await db.GetVersionAsync(vid, p.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
            var f = await db.GetVersionFileAsync(vid, path, ct);
            url = f?.Url is { } u ? SourceUrls.Resolve(s3, u) : s3.Presign(HttpMethod.Get, Buckets.Datasets, v.Prefix + path, TimeSpan.FromHours(1));
        }
        else url = s3.Presign(HttpMethod.Get, Buckets.Datasets, Buckets.Raw(p.WorkspaceId, p.DatasetId) + path, TimeSpan.FromHours(1));
        return new ItemView(s, url, schema?.Classes ?? "[]", await db.ListAnnotationsAsync(p.DatasetId, path, ct), await db.ListCommentsAsync(p.Id, path, ct));
    }
}
