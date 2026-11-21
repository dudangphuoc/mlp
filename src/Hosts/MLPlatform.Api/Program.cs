using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Text;
using MLPlatform.Api.Infrastructure;
using MLPlatform.Api.Services;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Generated;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

// `mlp-api probe <url>`: healthcheck in the chiseled image (no curl); 0 on HTTP 2xx.
if (args is ["probe", var probeUrl])
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    int code;
    try { code = (await http.GetAsync(probeUrl)).IsSuccessStatusCode ? 0 : 1; }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { code = 1; }
    Environment.Exit(code);
}

// `mlp-api db apply --root <db>`: apply migrations, then exit (db-migrate step, 20/09 §1)
if (args is ["db", "apply", ..])
{
    var root = args.SkipWhile(a => a != "--root").Skip(1).FirstOrDefault() ?? "db";
    var cs = Environment.GetEnvironmentVariable("MLP__Db__ConnectionString")
             ?? throw new InvalidOperationException("Missing MLP__Db__ConnectionString");
    await using var ds = new Npgsql.NpgsqlSlimDataSourceBuilder(cs).Build();
    var n = await new ScriptRunner(ds, Console.Out).ApplyAsync(ScriptRunner.Discover(root), CancellationToken.None);
    Console.WriteLine($"done: {n} new migration(s)");
    return;
}

var builder = WebApplication.CreateSlimBuilder(args);
builder.Configuration.AddEnvironmentVariables("MLP__");
var cfg = builder.Configuration;
var conn = cfg["Db:ConnectionString"] ?? throw new InvalidOperationException("Missing MLP__Db__ConnectionString");
var pepper = Encoding.UTF8.GetBytes(cfg["Secrets:TokenPepper"] ?? throw new InvalidOperationException("Missing MLP__Secrets__TokenPepper"));

var s = builder.Services;
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
s.AddScoped<IRequestBehavior, UnitOfWorkBehavior>();
s.AddScoped<IRequestBehavior, MLPlatform.ControlPlane.Application.Audit.AuditBehavior>();
s.AddGeneratedHandlers();
s.AddScoped<WorkspaceAuthorizer>();
s.AddScoped<MLPlatform.ControlPlane.Application.Labeling.LabelAccess>();
// S3 storage (D-34): SeaweedFS by default; Endpoint = used by the api, PublicEndpoint = browser, WorkloadEndpoint = containers on PCs
var s3Opts = new MLPlatform.Storage.S3Options(cfg["S3:Endpoint"] ?? "http://127.0.0.1:8333", cfg["S3:AccessKey"] ?? "",
    cfg["S3:SecretKey"] ?? "", cfg["S3:Region"] ?? "us-east-1", cfg["S3:PublicEndpoint"], cfg["S3:WorkloadEndpoint"]);
s.AddHttpClient("s3");
if (cfg["S3:DatasetsBucket"] is { Length: > 0 } dsb) MLPlatform.ControlPlane.Application.Data.Buckets.Datasets = dsb;
if (cfg["S3:ArtifactsBucket"] is { Length: > 0 } arb) MLPlatform.ControlPlane.Application.Data.Buckets.Artifacts = arb;
s.AddSingleton(sp => new MLPlatform.Storage.S3Client(sp.GetRequiredService<IHttpClientFactory>().CreateClient("s3"), s3Opts, TimeProvider.System));
s.AddSingleton<AgentRegistry>();
s.AddHostedService<AgentListener>();
s.AddSingleton<RecipeEnvironments>();
s.AddSingleton<WorkloadLauncher>();
s.AddSingleton<ReservationKeeper>();
s.AddHttpClient<MLPlatform.Ray.RayJobsClient>(c => c.BaseAddress = new Uri(cfg["Ray:DashboardUrl"] ?? "http://127.0.0.1:8265/"));
s.AddHostedService<SchedulerLoop>();
s.AddSingleton<ModelServerClients>();
s.AddSingleton<MLPlatform.ControlPlane.Application.LlmOps.ILlmInvoker, ModelServerLlmInvoker>();
s.AddHostedService<DeploymentProber>();
s.AddSingleton<DataChannelBroker>();
s.AddSingleton<NotebookProxy>();
s.AddHostedService<NotebookReaper>();
s.AddHostedService<DataCacheJanitor>();
s.AddSingleton<McpServer>();
s.AddHostedService<SweepLoop>();
s.AddHostedService<MonitorLoop>();
s.AddHostedService<PipelineLoop>();
s.AddHostedService<FeatureLoop>();
s.AddHostedService<RecoveryLoop>();
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
// Hugging Face Hub (search / import datasets + models; workspace token encrypted)
s.AddHttpClient("hf", c => { c.Timeout = Timeout.InfiniteTimeSpan; });
s.AddHttpClient("import", c => { c.Timeout = Timeout.InfiniteTimeSpan; c.DefaultRequestHeaders.UserAgent.ParseAdd("mlplatform-import/1.0"); });
s.AddHttpClient("s3-put", c => { c.Timeout = Timeout.InfiniteTimeSpan; });
s.AddSingleton<SecretBox>();
s.AddHttpClient("ai", c => { c.Timeout = Timeout.InfiniteTimeSpan; c.DefaultRequestHeaders.UserAgent.ParseAdd("mlplatform-ai/1.0"); });
s.AddSingleton<AiProviders>();
s.AddSingleton<AiCircuits>();
s.AddSingleton<AiLimiter>();
s.AddSingleton<AiRuntime>();
s.AddHostedService<AiRecoveryLoop>();
s.AddSingleton<ItemContent>();
s.AddSingleton<HermesLabelChecker>();
s.AddSingleton<LabelProposer>();
s.AddSingleton<HermesQuality>();
s.AddHostedService<PrelabelLoop>();
s.AddSingleton<HfHub>();
s.AddSingleton<HubImporter>();
s.AddHostedService(sp => sp.GetRequiredService<HubImporter>());
s.AddSingleton<MLPlatform.ControlPlane.Application.Hub.IHubImportQueue>(sp => sp.GetRequiredService<HubImporter>());
s.AddSingleton<AgentUpgrader>();
s.AddHostedService(sp => sp.GetRequiredService<AgentUpgrader>());
s.AddSingleton<StreamBroker>();
s.AddHostedService(sp => sp.GetRequiredService<StreamBroker>());
s.AddSingleton<LogStore>();
s.AddSingleton<SystemHealth>();
s.AddHostedService<HealthLoop>();
s.AddHttpClient();
s.AddSingleton(new MLPlatform.ControlPlane.Application.Ops.SignalOptions(
    TimeSpan.FromSeconds(int.TryParse(cfg["Recovery:SignalLostSeconds"], out var sl) && sl >= 30 ? sl : 120),
    TimeSpan.FromMinutes(int.TryParse(cfg["Recovery:SignalKillMinutes"], out var sk) && sk >= 1 ? sk : 30)));
s.AddSingleton(new MLPlatform.ControlPlane.Application.Ops.RecoveryOptions(
    TimeSpan.FromSeconds(int.TryParse(cfg["Recovery:GraceSeconds"], out var grace) && grace >= 10 ? grace : 300)));
s.AddGrpc(o => o.Interceptors.Add<ErrorInterceptor>());
// OpenTelemetry (P0-7, 20/09 §4): traces api → CQRS → Npgsql/HTTP, runtime/ASP.NET metrics; exports OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set.
var otel = s.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("mlp-api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource("MLPlatform.Cqrs", "Npgsql"))
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddMeter("Npgsql"));
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"))) otel.UseOtlpExporter();

var app = builder.Build();
app.Use((ctx, next) => ctx.RequestServices.GetRequiredService<PublicRoutes>().HandleAsync(ctx, next));   // subdomain nb-…/…--… (before routing)
app.UseRouting();
app.UseMlpAuth();
app.UseWebSockets();
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
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
// M08: Jupyter proxy over the data channel (an exception, not RPC; notebook owner only)
app.Map("/nb/{id:guid}/{**rest}", (HttpContext ctx, Guid id, NotebookProxy proxy) => proxy.HandleAsync(ctx, id));
app.MapGateway();
app.MapFeedback();
// MCP (Model Context Protocol) for external AI agents — Bearer API key; JSON responses only (no server-initiated stream).
app.MapPost("/mcp", (HttpContext h, McpServer mcp, CurrentUser cu) => mcp.HandleAsync(h, cu.UserId));
app.MapGet("/mcp", (HttpContext h) => { h.Response.StatusCode = 405; return Task.CompletedTask; });
app.MapGet("/health/live", () => "ok");
// An overlay hub on another machine (k8s) fetches the peer list here ("<pubkey> <ip>" per line); bearer WireGuard:PeersToken.
app.MapGet("/overlay/peers", async (HttpContext http, IServiceProvider sp, CancellationToken ct) =>
{
    var want = cfg["WireGuard:PeersToken"];
    var got = http.Request.Headers.Authorization.ToString();
    if (string.IsNullOrEmpty(want) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(got), System.Text.Encoding.UTF8.GetBytes("Bearer " + want)))
        return Results.StatusCode(401);
    await using var scope = sp.CreateAsyncScope();
    var peers = await scope.ServiceProvider.GetRequiredService<MLPlatform.Cqrs.IMediator>().Query(new MLPlatform.ControlPlane.Application.Fleet.ListOverlayPeersQuery(), ct);
    return Results.Text(string.Concat(peers.Select(p => $"{p.WgPublicKey} {p.OverlayIp}\n")), "text/plain");
});
// Ready to take load (compose healthcheck / load balancer): PostgreSQL answers within 2 s.
app.MapGet("/health/ready", async (MLPlatform.Data.DataSources db, CancellationToken ct) =>
{
    try
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        await using var c = await db.Write.OpenConnectionAsync(cts.Token);
        await using var cmd = new Npgsql.NpgsqlCommand("SELECT 1", c);
        await cmd.ExecuteScalarAsync(cts.Token);
        return Results.Text("ready");
    }
    catch (Exception e) when (e is Npgsql.NpgsqlException or OperationCanceledException or System.Net.Sockets.SocketException)
    {
        return Results.Text("db: " + e.Message, statusCode: 503);
    }
});
// 20/04 §2: install script + agent binary per RID (folder MLP__Install__AgentDir contains {rid}/mlp-agent + .sha256)
app.MapGet("/install/agent.sh", () =>
{
    var path = cfg["Install:ScriptPath"] ?? Path.Combine(AppContext.BaseDirectory, "install", "agent.sh");
    if (!File.Exists(path)) return Results.NotFound();
    var text = File.ReadAllText(path).Replace("__MLP_PUBLIC_URL__", cfg["PublicUrl"] ?? "").Replace("__MLP_AGENT_ADDR__", cfg["Install:AgentAddress"] ?? "")
        .Replace("__MLP_AGENT_PIN__", MLPlatform.Api.Infrastructure.AgentListener.CertPin(cfg) ?? "");
    return Results.Text(text, "text/x-shellscript");
});
app.MapGet("/install/agent.ps1", () =>
{
    var path = Path.Combine(Path.GetDirectoryName(cfg["Install:ScriptPath"] ?? Path.Combine(AppContext.BaseDirectory, "install", "agent.sh"))!, "agent.ps1");
    if (!File.Exists(path)) return Results.NotFound();
    var text = File.ReadAllText(path).Replace("__MLP_PUBLIC_URL__", cfg["PublicUrl"] ?? "").Replace("__MLP_AGENT_ADDR__", cfg["Install:AgentAddress"] ?? "")
        .Replace("__MLP_AGENT_PIN__", MLPlatform.Api.Infrastructure.AgentListener.CertPin(cfg) ?? "");
    return Results.Text(text, "text/plain; charset=utf-8");
});
app.MapAgentBuilds();

// D-27: empty DB → print a one-time bootstrap token to the log
await using (var scope = app.Services.CreateAsyncScope())
{
    // Admin account from configuration (.env): login always works after wiping the DB / running scenarios.
    if (cfg["Admin:Email"] is { Length: > 0 } adminEmail && cfg["Admin:Password"] is { Length: > 0 } adminPassword
        && await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new MLPlatform.ControlPlane.Application.Identity.EnsureConfiguredAdminCommand(
            adminEmail, adminPassword, cfg["Admin:Name"] ?? "Admin")))
        app.Logger.LogWarning("Created admin account {Email} from configuration", adminEmail);
    // D-40: machines kept working while the control plane was down → restart every watchdog clock before the background loops run.
    var restartGrace = TimeSpan.FromMinutes(double.TryParse(cfg["Recovery:RestartGraceMinutes"], System.Globalization.CultureInfo.InvariantCulture, out var gm) ? gm : 10);
    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new MLPlatform.ControlPlane.Application.Ops.RearmAfterRestartCommand(restartGrace));
    var token = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new IssueBootstrapTokenCommand());
    if (token is not null)
        app.Logger.LogWarning("Instance is not set up. Open /setup?token={Token} (expires in 24 hours)", token);
    // 20/05 §3: required buckets
    if (!string.IsNullOrEmpty(cfg["S3:AccessKey"]))
    {
        var s3 = scope.ServiceProvider.GetRequiredService<MLPlatform.Storage.S3Client>();
        // S3 storage may come up later than the api (compose) → retry for up to 60 s before giving up.
        foreach (var b in new[] { MLPlatform.ControlPlane.Application.Data.Buckets.Datasets, MLPlatform.ControlPlane.Application.Data.Buckets.Artifacts })
            for (var attempt = 1; ; attempt++)
            {
                try { await s3.EnsureBucketAsync(b, CancellationToken.None); break; }
                catch (HttpRequestException e) when (attempt < 30)
                {
                    Console.Error.WriteLine($"Waiting for S3 storage ({e.Message}); retry {attempt}/30");
                    await Task.Delay(2000);
                }
            }
    }
    // M17: sync builtin recipes + the release's task catalog
    if (cfg["Catalog:RecipesDir"] is { } recipesDir && Directory.Exists(recipesDir))
    {
        var n = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
            new MLPlatform.ControlPlane.Application.Catalog.SyncBuiltinRecipesCommand(recipesDir));
        app.Logger.LogInformation("Catalog: synced {Count} builtin recipe(s) from {Dir}", n, recipesDir);
    }
}

app.Run();
