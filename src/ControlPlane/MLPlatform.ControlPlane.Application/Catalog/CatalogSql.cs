using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Catalog;

public sealed record TaskRow(string Key, string ModalityIn, string ModalityOut, string[] Converters, string PrimaryMetric, string[] Metrics);

public sealed record RecipeRow(Guid Id, Guid? WorkspaceId, string Name, string DisplayName, string Family, string Description,
    string Maturity, string[] Tasks, string LicenseCode, string LicenseWeights, string LicenseNotes, bool RequiresAck, string Gpu,
    double MinVramGb, string Source, string Manifest, string State, string[] Selfcheck);

public static partial class CatalogSql
{
    [SqlQuery("Catalog/UpsertTask.sql")]
    public static partial Task UpsertTaskAsync(this IWriteSession db, string key, string modalityIn, string modalityOut,
        string[] converters, string primaryMetric, string[] metrics, CancellationToken ct);

    [SqlQuery("Catalog/ListTasks.sql")]
    public static partial Task<List<TaskRow>> ListTasksAsync(this IReadSession db, CancellationToken ct);

    [SqlQuery("Catalog/UpsertBuiltinRecipe.sql")]
    public static partial Task UpsertBuiltinRecipeAsync(this IWriteSession db, Guid id, string name, string displayName, string family,
        string description, string maturity, string[] tasks, string licenseCode, string licenseWeights, string licenseNotes,
        bool requiresAck, string gpu, double minVramGb, string source, string manifest, string manifestSha256, string state,
        string[] selfcheck, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Catalog/ListRecipes.sql")]
    public static partial Task<List<RecipeRow>> ListRecipesAsync(this IReadSession db, Guid workspaceId, string task, CancellationToken ct);

    [SqlQuery("Catalog/GetRecipe.sql")]
    public static partial Task<RecipeRow?> GetRecipeAsync(this IReadSession db, Guid workspaceId, string name, CancellationToken ct);

    [SqlQuery("Catalog/UpsertEnvironment.sql")]
    public static partial Task UpsertEnvironmentAsync(this IWriteSession db, Guid id, Guid workspaceId, string recipeName, string envTag,
        Guid hostId, string state, string error, DateTimeOffset startedAt, DateTimeOffset? builtAt, CancellationToken ct);

    [SqlQuery("Catalog/FinishEnvironment.sql")]
    public static partial Task FinishEnvironmentAsync(this IWriteSession db, Guid workspaceId, string envTag, Guid hostId, string state,
        string error, DateTimeOffset? builtAt, CancellationToken ct);

    [SqlQuery("Catalog/GetEnvironment.sql")]
    public static partial Task<EnvironmentRow?> GetEnvironmentAsync(this IReadSession db, Guid workspaceId, string envTag, Guid hostId, CancellationToken ct);

    [SqlQuery("Catalog/ListEnvironments.sql")]
    public static partial Task<List<EnvironmentRow>> ListEnvironmentsAsync(this IReadSession db, Guid workspaceId, string recipeName, CancellationToken ct);

    [SqlQuery("Catalog/GetInspection.sql")]
    public static partial Task<InspectionRow?> GetInspectionAsync(this IReadSession db, Guid workspaceId, string envTag, string variant,
        string paramsSha256, int numClasses, CancellationToken ct);

    [SqlQuery("Catalog/StartInspection.sql")]
    public static partial Task StartInspectionAsync(this IWriteSession db, Guid id, Guid workspaceId, string recipeName, string envTag,
        string variant, string paramsSha256, int numClasses, DateTimeOffset now, CancellationToken ct);

    [SqlQuery("Catalog/FinishInspection.sql")]
    public static partial Task FinishInspectionAsync(this IWriteSession db, Guid workspaceId, string envTag, string variant, string paramsSha256,
        int numClasses, string state, string error, string overlayYaml, string configJson, string blocks, string sources, CancellationToken ct);

    [SqlQuery("Catalog/DeleteInspection.sql")]
    public static partial Task DeleteInspectionAsync(this IWriteSession db, Guid workspaceId, string envTag, string variant, string paramsSha256,
        int numClasses, CancellationToken ct);
}
