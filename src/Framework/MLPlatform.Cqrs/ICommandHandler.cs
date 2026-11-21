namespace MLPlatform.Cqrs;

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    ValueTask<TResult> Handle(TCommand command, CancellationToken ct);
}
