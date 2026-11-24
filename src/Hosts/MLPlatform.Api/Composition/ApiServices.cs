using System.Text;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Generated;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MLPlatform.Api.Composition;

/// <summary>Registers every service of the control plane api; registration order is the hosted-service start
/// order.</summary>
public static class ApiServices
{
    public static void AddControlPlaneApi(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddDataAndRequestPipeline(cfg);
        s.AddStorage(cfg);
        s.AddAgentsAndScheduling(cfg);
        s.AddModulePorts(cfg);
        s.AddSchedulingLoops(cfg);
        s.AddServingAndNotebooks(cfg);
        s.AddWorkflowLoops(cfg);
        s.AddSettingsAndMessaging(cfg);
        s.AddHubAndAi(cfg);
        s.AddLabelingAssist(cfg);
        s.AddObservability(cfg);
        s.AddRecoveryOptions(cfg);
        s.AddGrpcAndTelemetry(cfg);
    }

    private static void AddDataAndRequestPipeline(this IServiceCollection s, IConfiguration cfg)
    {
        var conn = cfg["Db:ConnectionString"] ?? throw new InvalidOperationException("Missing MLP__Db__ConnectionString");
        var pepper = Encoding.UTF8.GetBytes(cfg["Secrets:TokenPepper"] ?? throw new InvalidOperationException("Missing MLP__Secrets__TokenPepper"));
        s.AddSingleton(TimeProvider.System);
        s.AddSingleton<IClock, SystemClock>();
        s.AddSingleton(new TokenService(pepper));
        s.AddSingleton(DataSources.Create(conn));
        s.AddScoped<ReadSession>();
        s.AddScoped<IReadSession>(sp => sp.GetRequiredService<ReadSession>());
        s.AddScoped<WriteSession>();
        s.AddScoped<IWriteSession>(sp => sp.GetRequiredService<WriteSession>());
        s.AddScoped<CurrentUser>();
        s.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        s.AddScoped<IRequestBehavior, MLPlatform.ControlPlane.Application.Common.ReadReleaseBehavior>();
        s.AddScoped<AfterCommitQueue>();
        s.AddScoped<MLPlatform.Cqrs.IAfterCommit>(sp => sp.GetRequiredService<AfterCommitQueue>());
        s.AddScoped<IRequestBehavior, UnitOfWorkBehavior>();
        s.AddScoped<MLPlatform.ControlPlane.Application.Audit.AuditDetails>();
        s.AddScoped<IRequestBehavior, MLPlatform.ControlPlane.Application.Audit.AuditBehavior>();
        s.AddScoped<IRequestBehavior, AuthorizationBehavior>();
        s.AddGeneratedHandlers();
        s.AddScoped<WorkspaceAuthorizer>();
        s.AddScoped<MLPlatform.ControlPlane.Application.Tracking.RemoteCodeRunner>();
        s.AddScoped<MLPlatform.ControlPlane.Application.Labeling.LabelAccess>();
    }

    private static void AddStorage(this IServiceCollection s, IConfiguration cfg)
    {
        var s3Opts = new MLPlatform.Storage.S3Options(cfg["S3:Endpoint"] ?? "http://127.0.0.1:8333", cfg["S3:AccessKey"] ?? "",
            cfg["S3:SecretKey"] ?? "", cfg["S3:Region"] ?? "us-east-1", cfg["S3:PublicEndpoint"], cfg["S3:WorkloadEndpoint"]);
        s.AddHttpClient("s3");
        if (cfg["S3:DatasetsBucket"] is { Length: > 0 } dsb) MLPlatform.ControlPlane.Application.Data.Buckets.Datasets = dsb;
        if (cfg["S3:ArtifactsBucket"] is { Length: > 0 } arb) MLPlatform.ControlPlane.Application.Data.Buckets.Artifacts = arb;
        s.AddSingleton(sp => new MLPlatform.Storage.S3Client(sp.GetRequiredService<IHttpClientFactory>().CreateClient("s3"), s3Opts, TimeProvider.System));
    }

    private static void AddAgentsAndScheduling(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddSingleton<AgentRegistry>();
        s.AddHostedService<AgentListener>();
        s.AddSingleton<RecipeEnvironments>();
        s.AddSingleton<RecipeSourceStore>();
        s.AddSingleton<CatalogSource>();
        s.AddSingleton<WorkloadLauncher>();
    }

    private static void AddModulePorts(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddScoped<MLPlatform.ControlPlane.Application.Scheduling.IJobWaitObserver, MLPlatform.ControlPlane.Application.Serving.DeploymentWaitObserver>();
        s.AddScoped<MLPlatform.ControlPlane.Application.Scheduling.IJobWaitObserver, MLPlatform.ControlPlane.Application.Notebooks.NotebookWaitObserver>();
        s.AddScoped<MLPlatform.ControlPlane.Application.Tracking.IRunDeletionGuard, MLPlatform.ControlPlane.Application.Registry.RegisteredRunGuard>();
        s.AddScoped<MLPlatform.ControlPlane.Application.Serving.IDeploymentDataOwner, MLPlatform.ControlPlane.Application.Monitoring.MonitoringDeploymentData>();
        s.AddScoped<MLPlatform.ControlPlane.Application.Serving.IDeploymentDataOwner, MLPlatform.ControlPlane.Application.LlmOps.LlmOpsDeploymentData>();
        s.AddScoped<MLPlatform.ControlPlane.Application.Serving.IDeploymentDataOwner, MLPlatform.ControlPlane.Application.Features.FeatureDeploymentData>();
        s.AddScoped<MLPlatform.ControlPlane.Application.Monitoring.IDriftListener, MLPlatform.ControlPlane.Application.Pipelines.DriftTriggeredPipelines>();
        s.AddScoped<MLPlatform.ControlPlane.Application.Fleet.IHostLossListener, MLPlatform.ControlPlane.Application.Ops.SuspectAttemptsOnHostLoss>();
        s.AddSingleton<MLPlatform.ControlPlane.Application.Scheduling.IWorkloadControl>(sp => sp.GetRequiredService<WorkloadLauncher>());
    }

    private static void AddSchedulingLoops(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddSingleton<ReservationKeeper>();
        s.AddHttpClient<MLPlatform.Ray.RayJobsClient>(c => c.BaseAddress = new Uri(cfg["Ray:DashboardUrl"] ?? "http://127.0.0.1:8265/"));
        s.AddHostedService<SchedulerLoop>();
    }

    private static void AddServingAndNotebooks(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddSingleton<ModelServerClients>();
        s.AddSingleton<MLPlatform.ControlPlane.Application.LlmOps.ILlmInvoker, ModelServerLlmInvoker>();
        s.AddHostedService<DeploymentProber>();
        s.AddSingleton<DataChannelBroker>();
        s.AddSingleton<MLPlatform.ControlPlane.Application.Fleet.Ops.IHostOps, AgentHostOps>();
        s.AddSingleton<MLPlatform.Api.Http.HostOps.TerminalSessions>();
        s.AddSingleton<MLPlatform.Api.Http.HostOps.HostTerminal>();
        s.AddSingleton<MLPlatform.Api.Http.HostOps.HostFilesHttp>();
        s.AddSingleton<NotebookProxy>();
        s.AddHostedService<NotebookReaper>();
        s.AddHostedService<DataCacheJanitor>();
        s.AddHostedService<GpuHistoryJanitor>();
        s.AddHostedService<CodeRunJanitor>();
        s.AddHostedService<MLPlatform.Api.Http.HostOps.TerminalRecordingJanitor>();
    }

    private static void AddWorkflowLoops(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddSingleton<McpServer>();
        s.AddHostedService<SweepLoop>();
        s.AddHostedService<MonitorLoop>();
        s.AddHostedService<PipelineLoop>();
        s.AddHostedService<FeatureLoop>();
        s.AddHostedService<RecoveryLoop>();
    }

    private static void AddSettingsAndMessaging(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddScoped<MLPlatform.ControlPlane.Application.Platform.InstanceAdminGuard>();
        s.AddSingleton<DynamicSettings>();
        PublicRoutes.Configure(cfg);
        s.AddSingleton<PublicRoutes>();
        s.AddHttpClient("telegram", c => c.Timeout = TimeSpan.FromSeconds(20));
        s.AddHttpClient("eval", c => c.Timeout = TimeSpan.FromSeconds(90));
        s.AddHostedService<EvaluationRunner>();
        s.AddSingleton<TelegramSender>();
        s.AddSingleton<MLPlatform.ControlPlane.Application.Identity.IEmailTransport, SmtpTransport>();
        s.AddHostedService<EmailSender>();
        s.AddHostedService<OverlayPeerSync>();
        s.AddSingleton<AgentBuildStore>();
    }

    private static void AddHubAndAi(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddHttpClient("hf", c => { c.Timeout = Timeout.InfiniteTimeSpan; });
        s.AddHttpClient("hf-noredirect", c => { c.Timeout = TimeSpan.FromSeconds(30); }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        s.AddHttpClient("import", c => { c.Timeout = Timeout.InfiniteTimeSpan; c.DefaultRequestHeaders.UserAgent.ParseAdd("mlplatform-import/1.0"); });
        s.AddHttpClient("s3-put", c => { c.Timeout = Timeout.InfiniteTimeSpan; });
        s.AddSingleton<SecretBox>();
        s.AddSingleton<MLPlatform.Api.Http.Debug.DebugRelay>();
        s.AddScoped<MLPlatform.ControlPlane.Application.Tracking.ICodeSnapshotComposer, CodeSnapshotComposer>();
        s.AddSingleton<MLPlatform.ControlPlane.Application.Tracking.ICodeEnvironment, CodeEnvImages>();
        s.AddScoped<MLPlatform.ControlPlane.Application.Tracking.IModelRefResolver, ModelRefResolver>();
        s.AddHttpClient("ai", c => { c.Timeout = Timeout.InfiniteTimeSpan; c.DefaultRequestHeaders.UserAgent.ParseAdd("mlplatform-ai/1.0"); });
        s.AddSingleton<AiProviders>();
        s.AddSingleton<AiCircuits>();
        s.AddSingleton<AiLimiter>();
        s.AddSingleton<AiRuntime>();
        s.AddHostedService<AiRecoveryLoop>();
    }

    private static void AddLabelingAssist(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddSingleton<ItemContent>();
        s.AddSingleton<HermesLabelChecker>();
        s.AddSingleton<LabelProposer>();
        s.AddSingleton<HermesQuality>();
        s.AddHostedService<PrelabelLoop>();
        s.AddSingleton<HfHub>();
        s.AddSingleton<HubImporter>();
        s.AddHostedService(sp => sp.GetRequiredService<HubImporter>());
        s.AddSingleton<MLPlatform.ControlPlane.Application.Hub.IHubImportQueue>(sp => sp.GetRequiredService<HubImporter>());
    }

    private static void AddObservability(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddSingleton<AgentUpgrader>();
        s.AddHostedService(sp => sp.GetRequiredService<AgentUpgrader>());
        s.AddSingleton<StreamBroker>();
        s.AddHostedService(sp => sp.GetRequiredService<StreamBroker>());
        s.AddSingleton<LogStore>();
        s.AddSingleton<SystemHealth>();
        s.AddHostedService<HealthLoop>();
        s.AddHttpClient();
    }

    private static void AddRecoveryOptions(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddSingleton(new MLPlatform.ControlPlane.Application.Ops.SignalOptions(
            TimeSpan.FromSeconds(int.TryParse(cfg["Recovery:SignalLostSeconds"], out var sl) && sl >= 30 ? sl : 120),
            TimeSpan.FromMinutes(int.TryParse(cfg["Recovery:SignalKillMinutes"], out var sk) && sk >= 1 ? sk : 30)));
        s.AddSingleton(new MLPlatform.ControlPlane.Application.Ops.RecoveryOptions(
            TimeSpan.FromSeconds(int.TryParse(cfg["Recovery:GraceSeconds"], out var grace) && grace >= 10 ? grace : 300)));
    }

    private static void AddGrpcAndTelemetry(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddGrpc(o => o.Interceptors.Add<ErrorInterceptor>());
        var otel = s.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("mlp-api"))
            .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource("MLPlatform.Cqrs", "Npgsql"))
            .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddMeter("Npgsql"));
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"))) otel.UseOtlpExporter();
    }
}
