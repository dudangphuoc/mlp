namespace MLPlatform.Cqrs;

/// <summary>Non-generic behavior for AOT safety; registration order = execution order (outer → inner).</summary>
public interface IRequestBehavior
{
    ValueTask<object?> Handle(RequestContext context, Func<ValueTask<object?>> next, CancellationToken ct);
}
