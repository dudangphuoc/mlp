namespace MLPlatform.Cqrs;

public sealed class ForbiddenException(string reason, string message) : AppException(reason, message);
