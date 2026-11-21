using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Common;

/// <summary>Commands run in one transaction; nested commands join the outermost one, which also drains the after-commit queue.</summary>
public sealed class UnitOfWorkBehavior(IWriteSession session, AfterCommitQueue afterCommit) : IRequestBehavior
{
    public async ValueTask<object?> Handle(RequestContext context, Func<ValueTask<object?>> next, CancellationToken ct)
    {
        if (!context.IsCommand || session.InTransaction) return await next();
        await session.BeginAsync(ct);
        object? result;
        try
        {
            result = await next();
            await session.CommitAsync(ct);
        }
        catch
        {
            await session.RollbackAsync(ct);
            afterCommit.Discard();
            throw;
        }
        await afterCommit.RunAsync(ct);
        return result;
    }
}
