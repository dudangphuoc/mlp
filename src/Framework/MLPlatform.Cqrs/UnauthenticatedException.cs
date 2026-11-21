namespace MLPlatform.Cqrs;

public sealed class UnauthenticatedException(string reason, string message) : AppException(reason, message);
