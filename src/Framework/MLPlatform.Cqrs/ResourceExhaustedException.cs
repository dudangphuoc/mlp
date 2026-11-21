namespace MLPlatform.Cqrs;

public sealed class ResourceExhaustedException(string reason, string message) : AppException(reason, message);
