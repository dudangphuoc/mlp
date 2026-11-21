using System.Net.Mail;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Email sending loop every 5 s (P0-18).</summary>
public sealed class EmailSender(IServiceScopeFactory scopes, IConfiguration cfg, SystemHealth health, ILogger<EmailSender> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stop))
        {
            health.Beat("email", 5);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var n = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new DispatchEmailsCommand(cfg["PublicUrl"] ?? ""), stop);
                if (n > 0) log.LogInformation("Sent {N} email(s)", n);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning("Sending email failed: {Msg}", e.Message); }
        }
    }
}
