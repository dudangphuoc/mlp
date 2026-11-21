using MLPlatform.ControlPlane.Infrastructure;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Services;

public sealed class RegistryGrpc(IMediator mediator) : RegistryService.RegistryServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    public override async Task<ModelVersion> RegisterFromRun(RegisterFromRunRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new RegisterFromRunCommand(G(r.WorkspaceId, "workspace_id"), G(r.RunId, "run_id"), r.ModelName, r.Description), ctx.CancellationToken));

    public override async Task<ListModelsResponse> ListModels(ListModelsRequest r, ServerCallContext ctx)
    {
        var res = new ListModelsResponse();
        res.Models.AddRange((await mediator.Query(new ListModelsQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(m => new RegisteredModel
        {
            Id = m.Id.ToString(), WorkspaceId = m.WorkspaceId.ToString(), Name = m.Name, Task = m.Task, LatestVersion = m.LatestVersion,
            ProductionVersion = m.ProductionVersion, CreatedAt = Timestamp.FromDateTimeOffset(m.CreatedAt),
        }));
        return res;
    }

    public override async Task<ListModelVersionsResponse> ListModelVersions(ListModelVersionsRequest r, ServerCallContext ctx)
    {
        var res = new ListModelVersionsResponse();
        res.Versions.AddRange((await mediator.Query(new ListModelVersionsQuery(G(r.WorkspaceId, "workspace_id"), G(r.ModelId, "model_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<ModelVersion> TransitionStage(TransitionStageRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new TransitionStageCommand(G(r.WorkspaceId, "workspace_id"), G(r.VersionId, "version_id"), r.Stage, r.Reason), ctx.CancellationToken));

    private static ModelVersion ToProto(ModelVersionRow v) => new()
    {
        Id = v.Id.ToString(), ModelId = v.ModelId.ToString(), ModelName = v.ModelName, Version = v.Version, RunId = v.RunId?.ToString() ?? "",
        Recipe = v.Recipe, Variant = v.Variant, Task = v.Task, Stage = v.Stage, FileCount = v.FileCount, SizeBytes = v.SizeBytes,
        MetricsJson = v.Metrics, CreatedAt = Timestamp.FromDateTimeOffset(v.CreatedAt), Source = v.Source, SourceRef = v.SourceRef ?? "", Storage = v.Storage,
        ParentVersionId = v.ParentVersionId?.ToString() ?? "", OptimizationJson = v.Optimization ?? "",
    };

    public override async Task<ListModelVersionsResponse> ListVersionsByRun(ListVersionsByRunRequest r, ServerCallContext ctx)
    {
        var res = new ListModelVersionsResponse();
        res.Versions.AddRange((await mediator.Query(new ListVersionsByRunQuery(G(r.WorkspaceId, "workspace_id"), G(r.RunId, "run_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    // ───────────── Evaluations ─────────────

    public override async Task<Evaluation> StartEvaluation(StartEvaluationRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new StartEvaluationCommand(G(r.WorkspaceId, "workspace_id"), G(r.ModelVersionId, "model_version_id"),
            G(r.DatasetVersionId, "dataset_version_id"), r.PathFilter, r.MaxSamples <= 0 ? 200 : r.MaxSamples, r.ConfThreshold <= 0 ? 0.25 : r.ConfThreshold), ctx.CancellationToken));

    public override async Task<ListEvaluationsResponse> ListEvaluations(ListEvaluationsRequest r, ServerCallContext ctx)
    {
        var res = new ListEvaluationsResponse();
        res.Evaluations.AddRange((await mediator.Query(new ListEvaluationsQuery(G(r.WorkspaceId, "workspace_id"), G(r.ModelVersionId, "model_version_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<Evaluation> GetEvaluation(GetEvaluationRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Query(new GetEvaluationQuery(G(r.WorkspaceId, "workspace_id"), G(r.EvaluationId, "evaluation_id")), ctx.CancellationToken));

    public override async Task<ListEvaluationSamplesResponse> ListEvaluationSamples(ListEvaluationSamplesRequest r, ServerCallContext ctx)
    {
        var res = new ListEvaluationSamplesResponse();
        foreach (var (x, url) in await mediator.Query(new ListEvalSamplesQuery(G(r.WorkspaceId, "workspace_id"), G(r.EvaluationId, "evaluation_id"),
                     r.ErrorsOnly, r.Offset, r.Limit <= 0 ? 24 : r.Limit), ctx.CancellationToken))
            res.Samples.Add(new EvaluationSample
            {
                Idx = x.Idx, Path = x.Path, ImageUrl = url, Width = x.Width, Height = x.Height, GtJson = x.Gt, PredJson = x.Pred,
                Tp = x.Tp, Fp = x.Fp, Fn = x.Fn, Correct = x.Correct, Error = x.Error ?? "", LatencyMs = x.LatencyMs,
            });
        return res;
    }

    public override async Task<Evaluation> CancelEvaluation(GetEvaluationRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var id = G(r.EvaluationId, "evaluation_id");
        await mediator.Send(new CancelEvaluationCommand(ws, id), ctx.CancellationToken);
        return ToProto(await mediator.Query(new GetEvaluationQuery(ws, id), ctx.CancellationToken));
    }

    private static Evaluation ToProto(EvaluationRow e) => new()
    {
        Id = e.Id.ToString(), ModelVersionId = e.ModelVersionId.ToString(), DatasetVersionId = e.DatasetVersionId.ToString(), PathFilter = e.PathFilter,
        MaxSamples = e.MaxSamples, ConfThreshold = e.ConfThreshold, DeploymentId = e.DeploymentId?.ToString() ?? "", State = e.State,
        StateReason = e.StateReason ?? "", Total = e.Total, Done = e.Done, Errors = e.Errors, MetricsJson = e.Metrics, PerClassJson = e.PerClass,
        ConfusionJson = e.Confusion ?? "", CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(e.CreatedAt),
        StartedAt = e.StartedAt is { } s ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(s) : null,
        FinishedAt = e.FinishedAt is { } f ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(f) : null,
    };
    public override async Task<ModelExport> StartExport(StartExportRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new StartExportCommand(G(r.WorkspaceId, "workspace_id"), G(r.ModelVersionId, "model_version_id"), r.Format, string.IsNullOrEmpty(r.HostId) ? null : G(r.HostId, "host_id")), ctx.CancellationToken));

    public override async Task<ListExportsResponse> ListExports(ListExportsRequest r, ServerCallContext ctx)
    {
        var (ws, version) = (G(r.WorkspaceId, "workspace_id"), G(r.ModelVersionId, "model_version_id"));
        var res = new ListExportsResponse();
        res.Exports.AddRange((await mediator.Send(new ListExportsCommand(ws, version), ctx.CancellationToken)).Select(ToProto));
        res.Formats.AddRange(await mediator.Query(new ListExportFormatsQuery(ws, version), ctx.CancellationToken));
        return res;
    }

    public override async Task<ExportDownload> GetExportDownload(ExportRef r, ServerCallContext ctx) =>
        new() { Url = await mediator.Query(new GetExportDownloadQuery(G(r.WorkspaceId, "workspace_id"), G(r.ExportId, "export_id")), ctx.CancellationToken) };

    public override async Task<ExportDownload> DeleteExport(ExportRef r, ServerCallContext ctx)
    {
        await mediator.Send(new DeleteExportCommand(G(r.WorkspaceId, "workspace_id"), G(r.ExportId, "export_id")), ctx.CancellationToken);
        return new ExportDownload();
    }

    private static ModelExport ToProto(ExportRow e) => new()
    {
        Id = e.Id.ToString(), ModelVersionId = e.ModelVersionId.ToString(), Format = e.Format, State = e.State, Error = e.Error,
        JobId = e.JobId?.ToString() ?? "", FileName = e.FileName, SizeBytes = e.SizeBytes, CreatedAt = Timestamp.FromDateTimeOffset(e.CreatedAt),
    };
    public override async Task<Optimization> StartOptimization(StartOptimizationRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new StartOptimizationCommand(G(r.WorkspaceId, "workspace_id"), G(r.SourceVersionId, "source_version_id"), r.Device,
            r.Candidates.ToList(), string.IsNullOrEmpty(r.HostId) ? null : G(r.HostId, "host_id"), G(r.CheckDatasetVersionId, "check_dataset_version_id"),
            r.CheckImages, string.IsNullOrEmpty(r.CalibDatasetVersionId) ? null : G(r.CalibDatasetVersionId, "calib_dataset_version_id"), r.CalibImages,
            r.Conf, r.CalibMethod), ctx.CancellationToken));

    public override async Task<ListOptimizationsResponse> ListOptimizations(ListOptimizationsRequest r, ServerCallContext ctx)
    {
        var res = new ListOptimizationsResponse();
        res.Optimizations.AddRange((await mediator.Send(new ListOptimizationsCommand(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<Optimization> GetOptimization(OptimizationRef r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new GetOptimizationCommand(G(r.WorkspaceId, "workspace_id"), G(r.Id, "id")), ctx.CancellationToken));

    public override async Task<ExportDownload> GetOptimizationFile(OptimizationFileRequest r, ServerCallContext ctx) =>
        new() { Url = await mediator.Query(new GetOptimizationFileQuery(G(r.WorkspaceId, "workspace_id"), G(r.Id, "id"), r.Path), ctx.CancellationToken) };

    public override async Task<ModelVersion> PromoteOptimization(PromoteOptimizationRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new PromoteOptimizationCommand(G(r.WorkspaceId, "workspace_id"), G(r.Id, "id"), r.Candidate), ctx.CancellationToken));

    public override async Task<ExportDownload> DeleteOptimization(OptimizationRef r, ServerCallContext ctx)
    {
        await mediator.Send(new DeleteOptimizationCommand(G(r.WorkspaceId, "workspace_id"), G(r.Id, "id")), ctx.CancellationToken);
        return new ExportDownload();
    }

    private static Optimization ToProto(OptimizationRow o)
    {
        var p = new Optimization
        {
            Id = o.Id.ToString(), SourceVersionId = o.SourceVersionId.ToString(), ModelName = o.ModelName, SourceVersion = o.SourceVersion, Task = o.Task,
            Device = o.Device, HostId = o.HostId?.ToString() ?? "", CalibDatasetVersionId = o.CalibDatasetVersionId?.ToString() ?? "",
            CheckDatasetVersionId = o.CheckDatasetVersionId.ToString(), Conf = o.Conf, State = o.State, Error = o.Error, JobId = o.JobId?.ToString() ?? "",
            ReportJson = o.Report, PromotedJson = o.Promoted, CreatedAt = Timestamp.FromDateTimeOffset(o.CreatedAt),
        };
        p.Candidates.AddRange(o.Candidates.Split(',', StringSplitOptions.RemoveEmptyEntries));
        return p;
    }
}
