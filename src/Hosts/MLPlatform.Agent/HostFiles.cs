using System.Diagnostics;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent;

/// <summary>Files + scripts in the agent's state folder (D-38).</summary>
public static class HostFiles
{
    public static string? Resolve(string root, string rel)
    {
        var full = Path.GetFullPath(Path.Combine(root, rel.TrimStart('/', '\\')));
        return full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    private static CommandResult Outside() => new() { Ok = false, ErrorReason = "FLEET_PATH_OUTSIDE_ROOT", Message = "Path is outside the agent folder" };

    public static async Task<CommandResult> ReadAsync(string root, FileRead r, CancellationToken ct)
    {
        if (Resolve(root, r.Path) is not { } p) return Outside();
        if (!File.Exists(p)) return new CommandResult { Ok = true, Exists = false };
        var max = r.MaxBytes > 0 ? r.MaxBytes : 1024 * 1024;
        await using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > max) fs.Seek(-max, SeekOrigin.End);
        var buf = new byte[Math.Min(max, fs.Length - fs.Position)];
        await fs.ReadExactlyAsync(buf, ct);
        return new CommandResult { Ok = true, Exists = true, Data = Google.Protobuf.ByteString.CopyFrom(buf) };
    }

    public static async Task<CommandResult> WriteAsync(string root, FileWrite w, CancellationToken ct)
    {
        if (Resolve(root, w.Path) is not { } p) return Outside();
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        await File.WriteAllBytesAsync(p + ".tmp", w.Content.ToByteArray(), ct);
        File.Move(p + ".tmp", p, overwrite: true);
        if (w.Executable && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(p, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new CommandResult { Ok = true };
    }

    public static CommandResult Delete(string root, FileDelete d)
    {
        if (Resolve(root, d.Path) is not { } p) return Outside();
        if (Directory.Exists(p)) Directory.Delete(p, d.Recursive);
        else if (File.Exists(p)) File.Delete(p);
        return new CommandResult { Ok = true };
    }

    /// <summary>Run a script.</summary>
    public static async Task<CommandResult> RunScriptAsync(string root, RunScript r, CancellationToken ct)
    {
        if (Resolve(root, r.Path) is not { } path || !File.Exists(path)) return Outside();
        var job = string.IsNullOrEmpty(r.JobName) ? "mlp-script" : new string(r.JobName.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
        var win = OperatingSystem.IsWindows();
        var shell = string.IsNullOrEmpty(r.Shell) ? (win ? "powershell" : "sh") : r.Shell;
        string[] inner = shell == "powershell"
            ? ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path, .. r.Args]
            : ["/bin/sh", path, .. r.Args];
        if (!r.Detached)
        {
            var (code, outp) = await RunAsync(inner[0], inner[1..], ct, TimeSpan.FromSeconds(r.TimeoutSeconds > 0 ? r.TimeoutSeconds : 120));
            return new CommandResult { Ok = code == 0, ExitCode = code, Output = Tail(outp) };
        }
        (string Exe, string[] Args)[] steps;
        if (win)
        {
            var tr = string.Join(' ', inner.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            steps = [("schtasks", ["/Create", "/TN", "MLPlatform\\" + job, "/TR", tr, "/SC", "ONCE", "/ST", "00:00", "/RU", "SYSTEM", "/RL", "HIGHEST", "/F"]),
                     ("schtasks", ["/Run", "/TN", "MLPlatform\\" + job])];
        }
        else if (OperatingSystem.IsMacOS()) steps = [("launchctl", ["submit", "-l", "io.mlplatform." + job, "--", .. inner])];
        else if (File.Exists("/run/systemd/system") || Directory.Exists("/run/systemd/system"))
            steps = [("systemd-run", ["--unit", job, "--collect", "--quiet", .. inner])];
        else steps = [("setsid", ["-f", .. inner])];
        var all = "";
        foreach (var (exe, args) in steps)
        {
            var (code, outp) = await RunAsync(exe, args, ct, TimeSpan.FromSeconds(60));
            all += outp;
            if (code != 0) return new CommandResult { Ok = false, ErrorReason = "FLEET_SCRIPT_FAILED", ExitCode = code, Message = Tail(outp) };
        }
        return new CommandResult { Ok = true, Output = Tail(all) };
    }

    private static string Tail(string s) => s.Length > 64 * 1024 ? s[^(64 * 1024)..] : s;

    private static async Task<(int, string)> RunAsync(string exe, string[] args, CancellationToken ct, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var o = p.StandardOutput.ReadToEndAsync(cts.Token);
        var e = p.StandardError.ReadToEndAsync(cts.Token);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } return (124, "timeout"); }
        return (p.ExitCode, (await o) + (await e));
    }
}
