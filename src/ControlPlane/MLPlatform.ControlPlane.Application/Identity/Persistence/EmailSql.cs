using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

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
