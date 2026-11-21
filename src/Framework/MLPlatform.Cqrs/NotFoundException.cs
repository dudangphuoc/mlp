namespace MLPlatform.Cqrs;

public sealed class NotFoundException(string reason, string message) : AppException(reason, message);
