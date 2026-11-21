namespace MLPlatform.Cqrs;

/// <summary>Typed errors; the gRPC interceptor maps them to a status + ErrorInfo.reason (20/03, 20/07 §3).</summary>
public abstract class AppException(string reason, string message, IReadOnlyDictionary<string, string>? metadata = null)
    : Exception(message)
{
    public string Reason { get; } = reason;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata ?? new Dictionary<string, string>();
}
