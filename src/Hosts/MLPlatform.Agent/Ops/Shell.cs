using System.Diagnostics;

namespace MLPlatform.Agent.Ops;

/// <summary>Runs a system tool with a time limit and returns exit code and combined output.</summary>
public static class Shell
{
    public static async Task<(int Code, string Out)> RunAsync(string exe, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process? p;
        try { p = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception e) { return (-1, e.Message); }
        if (p is null) return (-1, exe + " could not start");
        using (p)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var o = p.StandardOutput.ReadToEndAsync(cts.Token);
            var e = p.StandardError.ReadToEndAsync(cts.Token);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { p.Kill(true); } catch (InvalidOperationException) { }
                return (124, exe + " timed out");
            }
            return (p.ExitCode, (await o) + (await e));
        }
    }
}
