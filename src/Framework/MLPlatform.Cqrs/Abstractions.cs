namespace MLPlatform.Cqrs;

/// <summary>Write command; result <typeparamref name="TResult"/>. Registration + dispatch are generated (20/02 §4).</summary>
public interface ICommand<TResult>;

/// <summary>Internal command sent by background work (e.g. the sweep orchestrator); not audited even if the scope has a user; child commands are still audited normally.</summary>
public interface IInternalCommand;

/// <summary>Read-only query.</summary>
public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    ValueTask<TResult> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    ValueTask<TResult> Handle(TQuery query, CancellationToken ct);
}

public interface IMediator
{
    ValueTask<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default);
    ValueTask<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default);
}

/// <summary>Request info flowing through the pipeline (no reflection: the generator fills it in).</summary>
public sealed record RequestContext(object Request, string Name, bool IsCommand, string? Permission, Guid? WorkspaceId = null);

/// <summary>Non-generic behavior for AOT safety; registration order = execution order (outer → inner).</summary>
public interface IRequestBehavior
{
    ValueTask<object?> Handle(RequestContext context, Func<ValueTask<object?>> next, CancellationToken ct);
}

/// <summary>Declares the minimum permission for a command/query; AuthorizationBehavior reads it via RequestContext.Permission.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
