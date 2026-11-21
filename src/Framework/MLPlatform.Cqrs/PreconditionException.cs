namespace MLPlatform.Cqrs;

public sealed class PreconditionException(string reason, string message) : AppException(reason, message);
