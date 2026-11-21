namespace MLPlatform.Cqrs;

/// <summary>Runs the behavior chain around a handler. Called from the generated mediator.</summary>
public static class Pipeline
{
    /// <summary>OTel span source for each command/query (20/02 §4: Activity is the outermost behavior).</summary>
    public static readonly System.Diagnostics.ActivitySource Source = new("MLPlatform.Cqrs");

    public static async ValueTask<object?> Run(IReadOnlyList<IRequestBehavior> behaviors, RequestContext ctx,
        Func<ValueTask<object?>> handler, CancellationToken ct)
    {
        using var activity = Source.StartActivity(ctx.Request.GetType().Name);
        try { return await RunInner(behaviors, ctx, handler, ct); }
        catch (Exception e) when (activity is not null)
        {
            activity.SetStatus(System.Diagnostics.ActivityStatusCode.Error, e.GetType().Name);
            throw;
        }
    }

    private static ValueTask<object?> RunInner(IReadOnlyList<IRequestBehavior> behaviors, RequestContext ctx,
        Func<ValueTask<object?>> handler, CancellationToken ct)
    {
        Func<ValueTask<object?>> next = handler;
        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var b = behaviors[i];
            var inner = next;
            next = () => b.Handle(ctx, inner, ct);
        }
        return next();
    }
}
