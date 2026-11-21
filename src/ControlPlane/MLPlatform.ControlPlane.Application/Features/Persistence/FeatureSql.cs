using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Features;

public static partial class FeatureSql
{
    [SqlQuery("Feature/DeleteDeploymentBinding.sql")]
    public static partial Task DeleteDeploymentBindingAsync(this IWriteSession db, Guid deploymentId, CancellationToken ct);
    [SqlQuery("Feature/InsertEntity.sql")]
    public static partial Task InsertEntityAsync(this IWriteSession db, Guid id, Guid workspaceId, string name, string joinKey, string description, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Feature/ListEntities.sql")]
    public static partial Task<List<EntityRow>> ListEntitiesAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Feature/GetEntity.sql")]
    public static partial Task<EntityRow?> GetEntityAsync(this IReadSession db, Guid workspaceId, string key, CancellationToken ct);
    [SqlQuery("Feature/InsertView.sql")]
    public static partial Task<int> InsertViewAsync(this IWriteSession db, Guid id, Guid workspaceId, string name, Guid entityId, Guid sourceVersionId, string timestampColumn,
        string features, int ttlHours, bool online, int? materializeEveryMin, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Feature/GetView.sql")]
    public static partial Task<ViewRow?> GetViewAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Feature/ResolveView.sql")]
    public static partial Task<ViewRow?> ResolveViewAsync(this IReadSession db, Guid workspaceId, string name, int version, CancellationToken ct);
    [SqlQuery("Feature/ListViews.sql")]
    public static partial Task<List<ViewRow>> ListViewsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Feature/DueViews.sql")]
    public static partial Task<List<ViewRow>> DueViewsAsync(this IReadSession db, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Feature/ClearOffline.sql")]
    public static partial Task ClearOfflineAsync(this IWriteSession db, Guid viewId, CancellationToken ct);
    [SqlQuery("Feature/InsertOffline.sql")]
    public static partial Task InsertOfflineAsync(this IWriteSession db, Guid viewId, string[] keys, DateTimeOffset[] times, string[] vals, CancellationToken ct);
    [SqlQuery("Feature/RefreshOnline.sql")]
    public static partial Task<long> RefreshOnlineAsync(this IWriteSession db, Guid viewId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Feature/FinishMaterialize.sql")]
    public static partial Task FinishMaterializeAsync(this IWriteSession db, Guid id, string state, string? reason, long rows, long entities, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Feature/PointInTime.sql")]
    public static partial Task<List<PitRow>> PointInTimeAsync(this IReadSession db, string[] keys, DateTimeOffset[] times, Guid viewId, int ttlHours, CancellationToken ct);
    [SqlQuery("Feature/OnlineLookup.sql")]
    public static partial Task<List<OnlineRow>> OnlineLookupAsync(this IReadSession db, Guid viewId, string[] keys, CancellationToken ct);
    [SqlQuery("Feature/OnlineSample.sql")]
    public static partial Task<List<OnlineRow>> OnlineSampleAsync(this IReadSession db, Guid viewId, int limit, CancellationToken ct);
    [SqlQuery("Feature/UpsertBinding.sql")]
    public static partial Task UpsertBindingAsync(this IWriteSession db, Guid deploymentId, Guid workspaceId, Guid entityId, string features, Guid updatedBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Feature/GetBinding.sql")]
    public static partial Task<BindingRow?> GetBindingAsync(this IReadSession db, Guid deploymentId, CancellationToken ct);
}
