using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Queries for versions imported from HF Hub.</summary>
public static partial class HubVersionSql
{
    [SqlQuery("Registry/InsertHubModelVersion.sql")]
    public static partial Task InsertHubModelVersionAsync(this IWriteSession db, Guid id, Guid modelId, int version, string task, string prefix, Guid createdBy,
        DateTimeOffset now, string sourceRef, string source, CancellationToken ct);
    [SqlQuery("Registry/InsertVersionFileRef.sql")]
    public static partial Task InsertVersionFileRefAsync(this IWriteSession db, Guid versionId, string path, long sizeBytes, string url, CancellationToken ct);
    [SqlQuery("Registry/ListVersionFileRefs.sql")]
    public static partial Task<List<MLPlatform.ControlPlane.Application.Data.VersionFileRow>> ListVersionFileRefsAsync(this IReadSession db, Guid versionId, CancellationToken ct);
    [SqlQuery("Registry/SetVersionReference.sql")]
    public static partial Task SetVersionReferenceAsync(this IWriteSession db, Guid id, CancellationToken ct);
    [SqlQuery("Registry/SetModelVersionSize.sql")]
    public static partial Task SetModelVersionSizeAsync(this IWriteSession db, Guid id, long fileCount, long sizeBytes, CancellationToken ct);
}
