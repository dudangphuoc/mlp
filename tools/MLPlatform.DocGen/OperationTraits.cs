using Google.Protobuf.Reflection;

namespace MLPlatform.DocGen;

/// <summary>Effect, audience and access of a gRPC method, derived from its name, service and the requests it
/// sends.</summary>
public static class OperationTraits
{
    private static readonly string[] ComputePrefixes = ["Start", "Train", "Deploy", "Run", "Launch", "Evaluate", "Optimize", "Retry", "Resume"];
    private static readonly HashSet<string> InternalServices = ["RuntimeService"];

    public static string Effect(MethodDescriptor method, IEnumerable<RequestInfo> requests)
    {
        var name = method.Name;
        if (name.StartsWith("Delete", StringComparison.Ordinal) || name.StartsWith("Remove", StringComparison.Ordinal)) return "delete";
        if (ComputePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))) return "compute";
        if (requests.Any(r => r.IsCommand)) return "write";
        return name.StartsWith("Get", StringComparison.Ordinal) || name.StartsWith("List", StringComparison.Ordinal) || name.StartsWith("Watch", StringComparison.Ordinal) || !requests.Any()
            ? "read" : "write";
    }

    public static string Audience(MethodDescriptor method, IEnumerable<RequestInfo> requests) =>
        InternalServices.Contains(method.Service.Name) || requests.Any(r => r.Access is "WorkloadToken") ? "agent"
        : requests.Any() && requests.All(r => r.Access == "Internal") ? "internal"
        : "user";

    public static RequestInfo? Primary(IEnumerable<RequestInfo> requests) =>
        requests.OrderBy(r => r.Access == "WorkspacePermission" ? 0 : r.Permission is not null ? 1 : 2).FirstOrDefault();
}
