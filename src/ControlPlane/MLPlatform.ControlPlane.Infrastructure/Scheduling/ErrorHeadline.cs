using System.Text.RegularExpressions;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>
/// Finds the line of a container log that explains a failure, so the job, run or deployment can say "ModuleNotFoundError: No module named 'x'"
/// instead of "exited with code 1". Launchers (torchrun, subprocess) wrap the real error in their own exception — those wrappers are skipped.
/// </summary>
public static partial class ErrorHeadline
{
    private static readonly string[] Wrappers = ["ChildFailedError", "CalledProcessError", "ProcessRaisedException", "ProcessExitedException", "SystemExit"];

    // Last line of a Python traceback: optional "[rank0]: " style prefix, the exception type, then its message.
    [GeneratedRegex(@"^(?:\[[^\]]{1,24}\]:?\s*)?((?:[A-Za-z_][\w.]*\.)?[A-Z]\w*(?:Error|Exception|Interrupt)|KeyboardInterrupt)(?::\s*(.*))?$")]
    private static partial Regex ExceptionLine();

    // Plain "error: …" / "fatal: …" lines of non-Python tools.
    [GeneratedRegex(@"^(?:\[[^\]]{1,24}\]:?\s*)?(?:error|fatal|panic)\b\s*:\s*\S", RegexOptions.IgnoreCase)]
    private static partial Regex PlainErrorLine();

    /// <summary>The most recent real error in the last lines of a log, or null when nothing looks like one.</summary>
    public static string? From(IReadOnlyList<string> lines)
    {
        var from = Math.Max(0, lines.Count - 600);
        for (var i = lines.Count - 1; i >= from; i--)
        {
            var m = ExceptionLine().Match(lines[i].TrimEnd());
            if (!m.Success) continue;
            var type = m.Groups[1].Value;
            if (Wrappers.Any(w => type.EndsWith(w, StringComparison.Ordinal))) continue;
            var detail = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
            return Clip(detail.Length > 0 ? $"{type}: {detail}" : type);
        }
        for (var i = lines.Count - 1; i >= from; i--)
            if (PlainErrorLine().IsMatch(lines[i])) return Clip(lines[i].Trim());
        return null;
    }

    private static string Clip(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
