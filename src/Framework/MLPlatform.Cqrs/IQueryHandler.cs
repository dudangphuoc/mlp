namespace MLPlatform.Cqrs;

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    ValueTask<TResult> Handle(TQuery query, CancellationToken ct);
}
