namespace MLPlatform.Cqrs;

/// <summary>ABORTED with a specific reason (revision/etag conflict).</summary>
public sealed class AbortedException(string reason, string message) : AppException(reason, message);
