namespace MLPlatform.Cqrs;

public interface IMediator
{
    ValueTask<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default);
    ValueTask<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default);
}
