namespace MLPlatform.Cqrs;

/// <summary>INVALID_ARGUMENT with a module-specific reason (e.g. LABEL_SCHEMA_VIOLATION), optionally with per-field details.</summary>
public sealed class InvalidArgumentException(string reason, string message, IReadOnlyDictionary<string, string>? fields = null)
    : AppException(reason, message, fields);
