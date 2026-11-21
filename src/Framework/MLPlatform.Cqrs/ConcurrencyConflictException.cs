namespace MLPlatform.Cqrs;

public sealed class ConcurrencyConflictException(string message = "The data has changed; reload")
    : AppException("CONCURRENCY_CONFLICT", message);
