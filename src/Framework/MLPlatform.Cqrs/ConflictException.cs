namespace MLPlatform.Cqrs;

public sealed class ConflictException(string reason, string message) : AppException(reason, message);
