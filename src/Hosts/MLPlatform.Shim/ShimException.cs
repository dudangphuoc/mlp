using System.Text.Json.Nodes;
using MLPlatform.Recipes;

namespace MLPlatform.Shim;

public sealed class ShimException(int exitCode, string reason, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
    public string Reason { get; } = reason;
}
