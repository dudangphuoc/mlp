namespace MLPlatform.CodeGate;

public sealed record GateOptions(string Root, bool UpdateBaseline, string? MetricsOutput, string? CommentInventoryOutput)
{
    public static GateOptions Parse(string[] args) => new(
        Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "."),
        args.Contains("--update-baseline"),
        ValueAfter(args, "--metrics"),
        ValueAfter(args, "--comment-inventory"));

    private static string? ValueAfter(string[] args, string flag) =>
        Array.IndexOf(args, flag) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
