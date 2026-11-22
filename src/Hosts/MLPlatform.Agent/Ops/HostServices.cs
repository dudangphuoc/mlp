using System.Text.RegularExpressions;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Ops;

/// <summary>System services: systemd on Linux, the Service Control Manager on Windows, launchd on macOS.</summary>
public static partial class HostServices
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(45);

    [GeneratedRegex("^[A-Za-z0-9@._:\\-]{1,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();

    public static async Task<CommandResult> ListAsync(CancellationToken ct)
    {
        var listing = OperatingSystem.IsWindows() ? await WindowsAsync(ct) : OperatingSystem.IsMacOS() ? await LaunchdAsync(ct) : await SystemdAsync(ct);
        return listing is null ? OpsResult.Fail("FLEET_SERVICES_UNAVAILABLE", "No supported service manager") : new CommandResult { Ok = true, ServiceListing = listing };
    }

    private static async Task<ServiceListing?> SystemdAsync(CancellationToken ct)
    {
        var (code, outp) = await Shell.RunAsync("systemctl", ["list-units", "--type=service", "--all", "--no-legend", "--no-pager", "--plain"], Limit, ct);
        if (code != 0) return null;
        var (_, files) = await Shell.RunAsync("systemctl", ["list-unit-files", "--type=service", "--no-legend", "--no-pager"], Limit, ct);
        var startType = files.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(f => f.Length >= 2).GroupBy(f => f[0]).ToDictionary(g => g.Key, g => g.First()[1]);
        var listing = new ServiceListing { Manager = "systemd" };
        foreach (var f in outp.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries)).Where(f => f.Length >= 4))
            listing.Services.Add(new ServiceInfo { Name = f[0], State = f[2], SubState = f[3], Description = f.Length > 4 ? f[4] : "", StartType = startType.GetValueOrDefault(f[0], "") });
        return listing;
    }

    private static async Task<ServiceListing?> WindowsAsync(CancellationToken ct)
    {
        const string script = "Get-CimInstance Win32_Service | ForEach-Object { \"$($_.Name)|$($_.State)|$($_.StartMode)|$($_.DisplayName)\" }";
        var (code, outp) = await Shell.RunAsync("powershell.exe", ["-NoProfile", "-Command", script], Limit, ct);
        if (code != 0) return null;
        var listing = new ServiceListing { Manager = "windows" };
        foreach (var f in outp.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(l => l.Split('|', 4)).Where(f => f.Length == 4))
            listing.Services.Add(new ServiceInfo { Name = f[0], State = f[1].ToLowerInvariant(), StartType = f[2].ToLowerInvariant(), Description = f[3] });
        return listing;
    }

    private static async Task<ServiceListing?> LaunchdAsync(CancellationToken ct)
    {
        var (code, outp) = await Shell.RunAsync("launchctl", ["list"], Limit, ct);
        if (code != 0) return null;
        var listing = new ServiceListing { Manager = "launchd" };
        foreach (var f in outp.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split('\t')).Where(f => f.Length == 3))
            listing.Services.Add(new ServiceInfo { Name = f[2], State = f[0] == "-" ? "inactive" : "active", SubState = f[0] == "-" ? "" : "running" });
        return listing;
    }

    public static async Task<CommandResult> ControlAsync(ServiceControl c, CancellationToken ct)
    {
        if (!SafeName().IsMatch(c.Name) || c.Action is not ("start" or "stop" or "restart"))
            return OpsResult.Fail("FLEET_SERVICE_INVALID", "Invalid service or action");
        if (c.Name.StartsWith("mlplatform-agent", StringComparison.Ordinal) || c.Name.StartsWith("io.mlplatform.agent", StringComparison.Ordinal))
            return OpsResult.Fail("FLEET_SERVICE_PROTECTED", "The agent service cannot be controlled from here");
        var (exe, args) = Command(c);
        var (code, outp) = await Shell.RunAsync(exe, args, Limit, ct);
        return code == 0 ? new CommandResult { Ok = true } : OpsResult.Fail("FLEET_SERVICE_FAILED", outp.Trim());
    }

    private static (string, string[]) Command(ServiceControl c)
    {
        if (OperatingSystem.IsWindows())
        {
            var verb = c.Action switch { "start" => "Start-Service", "stop" => "Stop-Service", _ => "Restart-Service" };
            return ("powershell.exe", ["-NoProfile", "-Command", $"{verb} -Name '{c.Name}' -ErrorAction Stop"]);
        }
        if (OperatingSystem.IsMacOS())
            return ("launchctl", c.Action == "stop" ? ["stop", c.Name] : ["kickstart", "-k", "system/" + c.Name]);
        return ("systemctl", [c.Action, c.Name]);
    }
}
