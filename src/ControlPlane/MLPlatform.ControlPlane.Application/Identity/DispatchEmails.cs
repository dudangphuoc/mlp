using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>Sends a batch of due emails: on error → retry after 2^n minutes (up to 8 times, then `dead`).</summary>
public sealed record DispatchEmailsCommand(string PublicUrl) : ICommand<int>, IInternalCommand;

/// <summary>Handles DispatchEmailsCommand in one transaction (rows locked with SKIP LOCKED — safe with multiple api
/// instances).</summary>
public sealed class DispatchEmailsCommandHandler(IWriteSession db, IClock clock, IEmailTransport smtp) : ICommandHandler<DispatchEmailsCommand, int>
{
    public async ValueTask<int> Handle(DispatchEmailsCommand c, CancellationToken ct)
    {
        if (!await smtp.IsConfiguredAsync(ct)) return 0;
        var sent = 0;
        foreach (var e in await db.ClaimEmailsAsync(clock.UtcNow, ct))
        {
            try
            {
                await smtp.SendAsync(e.ToEmail, e.Subject, e.Body.Replace("{public_url}", c.PublicUrl.TrimEnd('/')), ct);
                await db.MarkEmailAsync(e.Id, "sent", clock.UtcNow, clock.UtcNow, null, ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var dead = e.Attempts + 1 >= 8;
                await db.MarkEmailAsync(e.Id, dead ? "dead" : "pending", clock.UtcNow, clock.UtcNow.AddMinutes(Math.Pow(2, e.Attempts)), ex.Message, ct);
            }
        }
        return sent;
    }
}
