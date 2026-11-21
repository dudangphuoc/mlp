using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Common;

/// <summary>Principal of the current request (filled by Hosting from cookie/Bearer).</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? SessionTokenHash { get; }
}

public static class CurrentUserExtensions
{
    public static Guid RequireUser(this ICurrentUser u) =>
        u.UserId ?? throw new UnauthenticatedException("IAM_UNAUTHENTICATED", "Sign-in required");
}

/// <summary>Commands run in a transaction; queries do not open one (20/02 §5.1).</summary>
/// <summary>Returns the read connection to the pool as soon as a query finishes, so long requests (inference, streams) do not pin it.</summary>
public sealed class ReadReleaseBehavior(IReadSession read) : IRequestBehavior
{
    public async ValueTask<object?> Handle(RequestContext context, Func<ValueTask<object?>> next, CancellationToken ct)
    {
        try { return await next(); }
        finally { if (!context.IsCommand) await read.ReleaseAsync(CancellationToken.None); }
    }
}

public sealed class UnitOfWorkBehavior(IWriteSession session) : IRequestBehavior
{
    public async ValueTask<object?> Handle(RequestContext context, Func<ValueTask<object?>> next, CancellationToken ct)
    {
        // Nested commands (e.g. StartTraining → SubmitJob) share the outermost command's transaction
        if (!context.IsCommand || session.InTransaction) return await next();
        await session.BeginAsync(ct);
        try
        {
            var result = await next();
            await session.CommitAsync(ct);
            return result;
        }
        catch
        {
            await session.RollbackAsync(ct);
            throw;
        }
    }
}
