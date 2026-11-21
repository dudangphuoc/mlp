namespace MLPlatform.Cqrs;

public sealed class ValidationException(IReadOnlyDictionary<string, string> fields)
    : AppException("VALIDATION_FAILED", "Invalid data", fields);
