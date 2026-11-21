using System.Globalization;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Ops;
using MLPlatform.Cqrs;
using MLPlatform.Storage;

namespace MLPlatform.Api.Startup;

/// <summary>One-time work before the background loops start; order matters (rearm watchdogs before any loop
/// runs).</summary>
public static class StartupTasks
{
    private const int BucketAttempts = 30;

    public static async Task RunAsync(WebApplication app, IConfiguration cfg)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await EnsureConfiguredAdminAsync(app, cfg, mediator);
        await RearmWatchdogsAsync(cfg, mediator);
        await IssueBootstrapTokenAsync(app, mediator);
        await EnsureBucketsAsync(cfg, scope.ServiceProvider);
        await SyncBuiltinRecipesAsync(app, cfg, mediator);
    }

    private static async Task EnsureConfiguredAdminAsync(WebApplication app, IConfiguration cfg, IMediator mediator)
    {
        if (cfg["Admin:Email"] is not { Length: > 0 } email || cfg["Admin:Password"] is not { Length: > 0 } password) return;
        if (await mediator.Send(new EnsureConfiguredAdminCommand(email, password, cfg["Admin:Name"] ?? "Admin")))
            app.Logger.LogWarning("Created admin account {Email} from configuration", email);
    }

    private static Task RearmWatchdogsAsync(IConfiguration cfg, IMediator mediator)
    {
        var graceMinutes = double.TryParse(cfg["Recovery:RestartGraceMinutes"], CultureInfo.InvariantCulture, out var minutes) ? minutes : 10;
        return mediator.Send(new RearmAfterRestartCommand(TimeSpan.FromMinutes(graceMinutes))).AsTask();
    }

    private static async Task IssueBootstrapTokenAsync(WebApplication app, IMediator mediator)
    {
        if (await mediator.Send(new IssueBootstrapTokenCommand()) is { } token)
            app.Logger.LogWarning("Instance is not set up. Open /setup?token={Token} (expires in 24 hours)", token);
    }

    private static async Task EnsureBucketsAsync(IConfiguration cfg, IServiceProvider services)
    {
        if (string.IsNullOrEmpty(cfg["S3:AccessKey"])) return;
        var s3 = services.GetRequiredService<S3Client>();
        foreach (var bucket in new[] { Buckets.Datasets, Buckets.Artifacts })
            await EnsureBucketWhenStorageIsUpAsync(s3, bucket);
    }

    private static async Task EnsureBucketWhenStorageIsUpAsync(S3Client s3, string bucket)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { await s3.EnsureBucketAsync(bucket, CancellationToken.None); return; }
            catch (HttpRequestException e) when (attempt < BucketAttempts)
            {
                Console.Error.WriteLine($"Waiting for S3 storage ({e.Message}); retry {attempt}/{BucketAttempts}");
                await Task.Delay(2000);
            }
        }
    }

    private static async Task SyncBuiltinRecipesAsync(WebApplication app, IConfiguration cfg, IMediator mediator)
    {
        if (cfg["Catalog:RecipesDir"] is not { } dir || !Directory.Exists(dir)) return;
        var count = await mediator.Send(new SyncBuiltinRecipesCommand(dir));
        app.Logger.LogInformation("Catalog: synced {Count} builtin recipe(s) from {Dir}", count, dir);
    }
}
