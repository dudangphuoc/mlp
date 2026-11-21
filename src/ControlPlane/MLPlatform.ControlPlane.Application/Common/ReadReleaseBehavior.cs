using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Common;

/// <summary>Commands run in a transaction; queries do not open one (20/02 §5.1).</summary>
public sealed class ReadReleaseBehavior(IReadSession read) : IRequestBehavior
{
    public async ValueTask<object?> Handle(RequestContext context, Func<ValueTask<object?>> next, CancellationToken ct)
    {
        try { return await next(); }
        finally { if (!context.IsCommand) await read.ReleaseAsync(CancellationToken.None); }
    }
}
