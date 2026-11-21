namespace MLPlatform.Cqrs;

/// <summary>Typed errors; the gRPC interceptor maps them to a status + ErrorInfo.reason (20/03, 20/07 §3).</summary>
public abstract class AppException(string reason, string message, IReadOnlyDictionary<string, string>? metadata = null)
    : Exception(message)
{
    public string Reason { get; } = reason;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata ?? new Dictionary<string, string>();
}

public sealed class NotFoundException(string reason, string message) : AppException(reason, message);
public sealed class ConflictException(string reason, string message) : AppException(reason, message);
public sealed class ForbiddenException(string reason, string message) : AppException(reason, message);
public sealed class UnauthenticatedException(string reason, string message) : AppException(reason, message);
public sealed class PreconditionException(string reason, string message) : AppException(reason, message);
public sealed class ResourceExhaustedException(string reason, string message) : AppException(reason, message);
public sealed class UnavailableException(string reason, string message) : AppException(reason, message);
public sealed class ConcurrencyConflictException(string message = "The data has changed; reload")
    : AppException("CONCURRENCY_CONFLICT", message);
public sealed class ValidationException(IReadOnlyDictionary<string, string> fields)
    : AppException("VALIDATION_FAILED", "Invalid data", fields);
/// <summary>INVALID_ARGUMENT with a module-specific reason (e.g. LABEL_SCHEMA_VIOLATION), optionally with per-field details.</summary>
public sealed class InvalidArgumentException(string reason, string message, IReadOnlyDictionary<string, string>? fields = null)
    : AppException(reason, message, fields);
/// <summary>ABORTED with a specific reason (revision/etag conflict).</summary>
public sealed class AbortedException(string reason, string message) : AppException(reason, message);
