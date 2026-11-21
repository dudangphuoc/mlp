namespace MLPlatform.Api.Grpc;

public static class GrpcApi
{
    public static void MapGrpcApi(this IEndpointRouteBuilder app)
    {
        app.MapGrpcService<IamGrpc>();
        app.MapGrpcService<WorkspaceGrpc>();
        app.MapGrpcService<InstanceGrpc>();
        app.MapGrpcService<FleetGrpc>();
        app.MapGrpcService<SchedulerGrpc>();
        app.MapGrpcService<RuntimeGrpc>();
        app.MapGrpcService<CatalogGrpc>();
        app.MapGrpcService<TrackingGrpc>();
        app.MapGrpcService<DataGrpc>();
        app.MapGrpcService<RegistryGrpc>();
        app.MapGrpcService<ServingGrpc>();
        app.MapGrpcService<AuditGrpc>();
        app.MapGrpcService<LabelingGrpc>();
        app.MapGrpcService<NotebookGrpc>();
        app.MapGrpcService<NotificationGrpc>();
        app.MapGrpcService<TuningGrpc>();
        app.MapGrpcService<MonitoringGrpc>();
        app.MapGrpcService<PipelineGrpc>();
        app.MapGrpcService<LlmOpsGrpc>();
        app.MapGrpcService<FeatureGrpc>();
        app.MapGrpcService<OpsGrpc>();
        app.MapGrpcService<HubGrpc>();
        app.MapGrpcService<SettingsGrpc>();
        app.MapGrpcService<AiGrpc>();
    }
}
