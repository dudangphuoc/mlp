using System.Diagnostics;

namespace MLPlatform.Agent.Gpu;

/// <summary>Runs nvidia-smi (MLP_NVIDIA_SMI overrides the executable; the verify stack points it at a fake).</summary>
public static class NvidiaSmi
{
    public static string Exe => Environment.GetEnvironmentVariable("MLP_NVIDIA_SMI") is { Length: > 0 } fake ? fake : "nvidia-smi";

    /// <summary>True when GPU containers are simulated with the mlp.gpus label instead of Docker --gpus.</summary>
    public static bool FakeGpus => Environment.GetEnvironmentVariable("MLP_FAKE_GPU") == "1";

    /// <summary>Exit code + stdout + stderr; code -1 when the executable is missing, 124 on timeout.</summary>
    public static async Task<(int Code, string Out, string Err)> RunAsync(string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(Exe) { RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return (-1, "", "");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var o = p.StandardOutput.ReadToEndAsync(cts.Token);
            var e = p.StandardError.ReadToEndAsync(cts.Token);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { p.Kill(true); } catch (InvalidOperationException) { }
                return (124, "", "nvidia-smi did not answer within 10 s");
            }
            return (p.ExitCode, await o, await e);
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, "", ""); }
    }
}
