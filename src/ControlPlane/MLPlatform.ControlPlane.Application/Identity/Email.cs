using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>An email waiting to be sent.</summary>
public sealed record EmailRow(Guid Id, string ToEmail, string Subject, string Body, string Kind, int Attempts);

/// <summary>Email queue queries (P0-18).</summary>
public static partial class EmailSql
{
    [SqlQuery("Identity/QueueEmail.sql")]
    public static partial Task QueueEmailAsync(this IWriteSession db, Guid id, string toEmail, string subject, string body, string kind, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Identity/ClaimEmails.sql")]
    public static partial Task<List<EmailRow>> ClaimEmailsAsync(this IWriteSession db, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Identity/MarkEmail.sql")]
    public static partial Task MarkEmailAsync(this IWriteSession db, Guid id, string state, DateTimeOffset now, DateTimeOffset notBefore, string? error, CancellationToken ct);
}

/// <summary>Email sending gateway (SMTP per the dynamic "email" config in api; when disabled → nothing is sent, emails stay queued and the Admin copies links manually — D-27).</summary>
public interface IEmailTransport
{
    Task<bool> IsConfiguredAsync(CancellationToken ct);
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>Sends a batch of due emails: on error → retry after 2^n minutes (up to 8 times, then `dead`).</summary>
public sealed record DispatchEmailsCommand(string PublicUrl) : ICommand<int>, IInternalCommand;

/// <summary>Handles <see cref="DispatchEmailsCommand"/> in one transaction (rows locked with SKIP LOCKED — safe with multiple api instances).</summary>
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
