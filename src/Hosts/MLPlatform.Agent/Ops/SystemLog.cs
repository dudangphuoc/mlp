using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Ops;

/// <summary>Tail of the system journal (journalctl), Windows System event log, or macOS unified log.</summary>
public static class SystemLog
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    public static async Task<CommandResult> ReadAsync(SystemLogRead r, CancellationToken ct)
    {
        var lines = Math.Clamp(r.Lines > 0 ? r.Lines : 200, 1, 2000);
        var unit = r.Unit.Trim();
        if (unit.Length > 0 && unit.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || "@._:-".Contains(ch))))
            return OpsResult.Fail("FLEET_SERVICE_INVALID", "Invalid unit");
        var (exe, args) = Command(unit, lines);
        var (code, outp) = await Shell.RunAsync(exe, args, Limit, ct);
        if (code != 0) return OpsResult.Fail("FLEET_LOG_FAILED", outp.Trim());
        return new CommandResult { Ok = true, Output = outp.Length > 512 * 1024 ? outp[^(512 * 1024)..] : outp };
    }

    private static (string, string[]) Command(string unit, int lines)
    {
        var n = lines.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (OperatingSystem.IsWindows())
        {
            var source = unit.Length > 0 ? $" | Where-Object {{ $_.ProviderName -eq '{unit}' }}" : "";
            var script = $"Get-WinEvent -LogName System -MaxEvents {n}{source} | ForEach-Object {{ \"$($_.TimeCreated.ToString('s')) $($_.LevelDisplayName) $($_.ProviderName): $($_.Message -replace '\\s+',' ')\" }}";
            return ("powershell.exe", ["-NoProfile", "-Command", script]);
        }
        if (OperatingSystem.IsMacOS()) return ("log", ["show", "--last", "10m", "--style", "compact"]);
        return ("journalctl", unit.Length > 0 ? ["-u", unit, "-n", n, "--no-pager", "-o", "short-iso"] : ["-n", n, "--no-pager", "-o", "short-iso"]);
    }
}
