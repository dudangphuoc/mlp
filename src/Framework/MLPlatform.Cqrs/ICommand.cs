namespace MLPlatform.Cqrs;

/// <summary>Write command; result <typeparamref name="TResult"/>. Registration + dispatch are generated (20/02 §4).</summary>
public interface ICommand<TResult>;
