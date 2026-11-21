namespace MLPlatform.Cqrs;

public sealed class UnavailableException(string reason, string message) : AppException(reason, message);
