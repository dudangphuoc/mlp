using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Serving;

public static partial class ServingSql
{
    [SqlQuery("Serving/InsertDeployment.sql")]
    public static partial Task InsertDeploymentAsync(this IWriteSession db, Guid id, Guid workspaceId, string slug, Guid modelVersionId,
        string modelName, int modelVersion, string task, Guid jobId, Guid createdBy, DateTimeOffset now, string purpose, int? idleStopMin,
        double? cpus, int? gpus, string? imageOverride, CancellationToken ct);
    [SqlQuery("Serving/RestartDeployment.sql")]
    public static partial Task RestartDeploymentAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid modelVersionId, string modelName, int modelVersion,
        string task, Guid jobId, string purpose, int? idleStopMin, double? cpus, int? gpus, string? imageOverride, CancellationToken ct);
    [SqlQuery("Serving/ListDeployments.sql")]
    public static partial Task<List<DeploymentRow>> ListDeploymentsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Serving/GetDeployment.sql")]
    public static partial Task<DeploymentRow?> GetDeploymentAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Serving/GetDeploymentBySlug.sql")]
    public static partial Task<DeploymentRow?> GetDeploymentBySlugAsync(this IReadSession db, Guid workspaceId, string slug, CancellationToken ct);
    [SqlQuery("Serving/GetDeploymentByJob.sql")]
    public static partial Task<DeploymentRow?> GetDeploymentByJobAsync(this IReadSession db, Guid jobId, CancellationToken ct);
    [SqlQuery("Serving/SetDeploymentState.sql")]
    public static partial Task SetDeploymentStateAsync(this IWriteSession db, Guid id, string state, string? reason, string? endpoint, CancellationToken ct);
    [SqlQuery("Serving/ListDeployingWithEndpoint.sql")]
    public static partial Task<List<DeploymentRow>> ListDeployingWithEndpointAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Serving/FindActiveForVersion.sql")]
    public static partial Task<DeploymentRow?> FindActiveForVersionAsync(this IReadSession db, Guid workspaceId, Guid modelVersionId, CancellationToken ct);
    [SqlQuery("Serving/TouchDeployment.sql")]
    public static partial Task TouchDeploymentAsync(this IWriteSession db, Guid id, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Serving/ListIdleTestDeployments.sql")]
    public static partial Task<List<DeploymentRow>> ListIdleTestDeploymentsAsync(this IReadSession db, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Serving/InsertEndpointKey.sql")]
    public static partial Task InsertEndpointKeyAsync(this IWriteSession db, Guid id, Guid deploymentId, string name, string prefix, string hash,
        Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Serving/DeleteDeployment.sql")]
    public static partial Task DeleteDeploymentAsync(this IWriteSession db, Guid id, CancellationToken ct);
    [SqlQuery("Serving/ListEndpointKeys.sql")]
    public static partial Task<List<EndpointKeyRow>> ListEndpointKeysAsync(this IReadSession db, Guid deploymentId, CancellationToken ct);
    [SqlQuery("Serving/RevokeEndpointKey.sql")]
    public static partial Task RevokeEndpointKeyAsync(this IWriteSession db, Guid id, Guid deploymentId, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Serving/GetKeyDeployment.sql")]
    public static partial Task<Guid?> GetKeyDeploymentAsync(this IReadSession db, string hash, CancellationToken ct);
}
