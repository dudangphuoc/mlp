using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Common;

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
