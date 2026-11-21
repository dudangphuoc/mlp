using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Pipelines;

public static partial class PipelineSql
{
    [SqlQuery("Pipeline/InsertPipeline.sql")]
    public static partial Task InsertPipelineAsync(this IWriteSession db, Guid id, Guid workspaceId, string name, string definition, string trigger,
        Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Pipeline/GetPipeline.sql")]
    public static partial Task<PipelineRow?> GetPipelineAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Pipeline/ListPipelines.sql")]
    public static partial Task<List<PipelineRow>> ListPipelinesAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Pipeline/ListTriggered.sql")]
    public static partial Task<List<PipelineRow>> ListTriggeredAsync(this IReadSession db, DateTimeOffset now, string eventKind, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Pipeline/MarkTriggered.sql")]
    public static partial Task MarkTriggeredAsync(this IWriteSession db, Guid id, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Pipeline/SetPipelineEnabled.sql")]
    public static partial Task SetPipelineEnabledAsync(this IWriteSession db, Guid id, bool enabled, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Pipeline/InsertRun.sql")]
    public static partial Task<int> InsertPipelineRunAsync(this IWriteSession db, Guid id, Guid pipelineId, Guid workspaceId, string runParams, string trigger,
        Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Pipeline/GetRun.sql")]
    public static partial Task<PipelineRunRow?> GetPipelineRunAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Pipeline/ListRuns.sql")]
    public static partial Task<List<PipelineRunRow>> ListPipelineRunsAsync(this IReadSession db, Guid workspaceId, Guid? pipelineId, CancellationToken ct);
    [SqlQuery("Pipeline/ListActiveRuns.sql")]
    public static partial Task<List<PipelineRunRow>> ListActivePipelineRunsAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Pipeline/FinishRun.sql")]
    public static partial Task FinishPipelineRunAsync(this IWriteSession db, Guid id, string state, string? error, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Pipeline/ReopenRun.sql")]
    public static partial Task ReopenPipelineRunAsync(this IWriteSession db, Guid id, string runParams, CancellationToken ct);
    [SqlQuery("Pipeline/InsertStep.sql")]
    public static partial Task InsertStepAsync(this IWriteSession db, Guid runId, string stepId, string kind, CancellationToken ct);
    [SqlQuery("Pipeline/ListSteps.sql")]
    public static partial Task<List<StepRow>> ListStepsAsync(this IReadSession db, Guid runId, CancellationToken ct);
    [SqlQuery("Pipeline/UpdateStep.sql")]
    public static partial Task UpdateStepAsync(this IWriteSession db, Guid runId, string stepId, string state, int attempt, Guid? refId, string output,
        string? error, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Pipeline/ResetStep.sql")]
    public static partial Task ResetStepAsync(this IWriteSession db, Guid runId, string stepId, CancellationToken ct);
}
