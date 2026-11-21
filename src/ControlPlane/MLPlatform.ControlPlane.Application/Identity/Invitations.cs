using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

public sealed record InvitationRow(Guid Id, Guid WorkspaceId, string Email, string Role, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt);
public sealed record MemberRow(Guid UserId, string Username, string DisplayName, string Email, string Role);

public static partial class InvitationSql
{
    [SqlQuery("Identity/InsertInvitation.sql")]
    public static partial Task InsertInvitationAsync(this IWriteSession db, Guid id, Guid workspaceId, string email, string role, string tokenHash,
        Guid createdBy, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken ct);
    [SqlQuery("Identity/GetInvitation.sql")]
    public static partial Task<InvitationRow?> GetInvitationAsync(this IWriteSession db, string tokenHash, CancellationToken ct);
    [SqlQuery("Identity/AcceptInvitation.sql")]
    public static partial Task AcceptInvitationAsync(this IWriteSession db, Guid id, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Identity/ListMembers.sql")]
    public static partial Task<List<MemberRow>> ListMembersAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
}

/// <summary>M01 §5.3: invite to a workspace with a role; one-time `mlpi_` token valid for 7 days (the invitation email is queued in identity.email_outbox; the token is still returned so the inviter can copy it when SMTP is not set up).</summary>
public sealed record CreateInvitationCommand(Guid WorkspaceId, string Email, string Role) : ICommand<(string Token, DateTimeOffset ExpiresAt)>;

public sealed class CreateInvitationCommandHandler(IWriteSession db, IClock clock, TokenService tokens, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateInvitationCommand, (string Token, DateTimeOffset ExpiresAt)>
{
    public static readonly string[] Roles = ["admin", "editor", "reviewer", "annotator", "viewer"];

    public async ValueTask<(string Token, DateTimeOffset ExpiresAt)> Handle(CreateInvitationCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "member.manage", ct);
        if (!Roles.Contains(c.Role)) throw new ValidationException(new Dictionary<string, string> { ["role"] = string.Join(" | ", Roles) });
        if (!c.Email.Contains('@')) throw new ValidationException(new Dictionary<string, string> { ["email"] = "Invalid email" });
        var token = TokenService.New("mlpi_");
        var now = clock.UtcNow;
        var exp = now.AddDays(7);
        var email = c.Email.Trim().ToLowerInvariant();
        await db.InsertInvitationAsync(Ids.New(), c.WorkspaceId, email, c.Role, tokens.Hash(token), uid, now, exp, ct);
        await db.QueueEmailAsync(Ids.New(), email, "Invitation to join MLPlatform",
            $"You have been invited to a workspace on MLPlatform with the role {c.Role}.\n\nOpen {{public_url}}/#/signup?invite={token} to create your account (expires {exp:yyyy-MM-dd}).\n",
            "invitation", now, ct);
        return (token, exp);
    }
}

internal static class InvitationRules
{
    /// <summary>Checks the invitation before creating the account (token used/expired/wrong email → IAM_INVITATION_INVALID).</summary>
    public static async Task<InvitationRow> ValidateAsync(IWriteSession db, IClock clock, TokenService tokens, string token, string email, CancellationToken ct)
    {
        var inv = await db.GetInvitationAsync(tokens.Hash(token), ct);
        if (inv is null || inv.AcceptedAt is not null || inv.ExpiresAt < clock.UtcNow || inv.Email != email)
            throw new PreconditionException("IAM_INVITATION_INVALID", "Invitation is invalid or has expired");
        return inv;
    }

    public static async Task AcceptAsync(IWriteSession db, IClock clock, InvitationRow inv, Guid userId, CancellationToken ct)
    {
        await db.InsertMembershipAsync(inv.WorkspaceId, userId, inv.Role, clock.UtcNow, ct);
        await db.AcceptInvitationAsync(inv.Id, clock.UtcNow, ct);
    }
}

public sealed record ListMembersQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<MemberRow>>;

public sealed class ListMembersQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListMembersQuery, IReadOnlyList<MemberRow>>
{
    public async ValueTask<IReadOnlyList<MemberRow>> Handle(ListMembersQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListMembersAsync(q.WorkspaceId, ct);
    }
}
