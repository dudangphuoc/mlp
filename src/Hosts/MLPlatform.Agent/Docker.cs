using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent;

/// <summary>
/// Container control via the Docker CLI (works the same on all 3 OSes). Agent v2 (D-38) does not know what a container is used for;
/// it only translates a <see cref="ContainerSpec"/> into <c>docker run</c>; every platform container has the label <c>mlp.managed=1</c>
/// and <c>mlp.spec</c> (spec hash) so <c>ContainerApply</c> knows when to keep it as is.
/// </summary>
public static class Docker
{
    public static async Task<(int Code, string Out, string Err)> RunAsync(IEnumerable<string> args, CancellationToken ct, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not run docker");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) cts.CancelAfter(t);
        var outTask = p.StandardOutput.ReadToEndAsync(cts.Token);
        var errTask = p.StandardError.ReadToEndAsync(cts.Token);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } throw; }
        return (p.ExitCode, (await outTask).Trim(), (await errTask).Trim());
    }

    /// <summary>Like <see cref="RunAsync"/>, but every output line is passed to <paramref name="onLine"/> as it arrives (progress of long steps).</summary>
    public static async Task<(int Code, string Tail)> RunStreamingAsync(IEnumerable<string> args, Action<string> onLine, CancellationToken ct, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = new Process { StartInfo = psi };
        var tail = new Queue<string>();
        void Take(string? line)
        {
            if (line is null) return;
            lock (tail) { tail.Enqueue(line); while (tail.Count > 40) tail.Dequeue(); }
            onLine(line);
        }
        p.OutputDataReceived += (_, e) => Take(e.Data);
        p.ErrorDataReceived += (_, e) => Take(e.Data);
        if (!p.Start()) throw new InvalidOperationException("Could not run docker");
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) cts.CancelAfter(t);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } throw; }
        lock (tail) return (p.ExitCode, string.Join("\n", tail));
    }

    /// <summary>Spec hash (stable protobuf format), the <c>mlp.spec</c> label.</summary>
    public static string SpecHash(ContainerSpec s) => Convert.ToHexStringLower(SHA256.HashData(Google.Protobuf.MessageExtensions.ToByteArray(s)))[..16];

    /// <summary><c>docker run -d</c> (or <c>--rm</c> run-once) from a spec.</summary>
    public static List<string> RunArgs(ContainerSpec s, bool once)
    {
        string I(double v) => v.ToString(CultureInfo.InvariantCulture);
        var args = new List<string> { "run" };
        args.Add(once ? "--rm" : "-d");
        if (!string.IsNullOrEmpty(s.Name)) args.AddRange(["--name", s.Name]);
        args.AddRange(["--label", "mlp.managed=1", "--label", "mlp.spec=" + SpecHash(s)]);
        foreach (var (k, v) in s.Labels.OrderBy(x => x.Key, StringComparer.Ordinal)) args.AddRange(["--label", $"{k}={v}"]);
        if (!string.IsNullOrEmpty(s.Network)) args.AddRange(["--network", s.Network]);
        if (!string.IsNullOrEmpty(s.Restart) && !once) args.AddRange(["--restart", s.Restart]);
        if (s.Init) args.Add("--init");
        if (s.Cpus > 0) args.AddRange(["--cpus", I(s.Cpus)]);
        if (s.MemoryBytes > 0) args.AddRange(["--memory", s.MemoryBytes.ToString(CultureInfo.InvariantCulture)]);
        if (s.ShmBytes > 0) args.AddRange(["--shm-size", s.ShmBytes.ToString(CultureInfo.InvariantCulture)]);
        if (s.Gpus.Count == 1 && s.Gpus[0] == "all") args.AddRange(["--gpus", "all"]);
        else if (s.Gpus.Count > 0) args.AddRange(["--gpus", "\"device=" + string.Join(',', s.Gpus) + "\""]);
        foreach (var c in s.CapAdd) args.AddRange(["--cap-add", c]);
        foreach (var (k, v) in s.Sysctls) args.AddRange(["--sysctl", $"{k}={v}"]);
        foreach (var p in s.Ports)
        {
            var host = p.HostPort > 0 ? p.HostPort.ToString(CultureInfo.InvariantCulture) : "";
            var spec = string.IsNullOrEmpty(p.HostIp) ? $"{host}:{p.ContainerPort}" : $"{p.HostIp}:{host}:{p.ContainerPort}";
            args.AddRange(["-p", string.IsNullOrEmpty(p.Protocol) ? spec : spec + "/" + p.Protocol]);
        }
        foreach (var (k, v) in s.Env.OrderBy(x => x.Key, StringComparer.Ordinal)) args.AddRange(["-e", $"{k}={v}"]);
        foreach (var m in s.Mounts) args.AddRange(["-v", $"{m.Source}:{m.Target}{(m.ReadOnly ? ":ro" : "")}"]);
        if (!string.IsNullOrEmpty(s.Workdir)) args.AddRange(["-w", s.Workdir]);
        if (s.Entrypoint.Count > 0) args.AddRange(["--entrypoint", s.Entrypoint[0]]);
        args.Add(s.Image);
        args.AddRange(s.Entrypoint.Skip(1));
        args.AddRange(s.Command);
        return args;
    }

    /// <summary>docker run error → reason code for the control plane.</summary>
    public static string Reason(string err) =>
        err.Contains("pull access denied", StringComparison.OrdinalIgnoreCase) || err.Contains("Unable to find image", StringComparison.OrdinalIgnoreCase)
        || err.Contains("manifest unknown", StringComparison.OrdinalIgnoreCase) ? "FLEET_IMAGE_PULL_FAILED" : "FLEET_DOCKER_FAILED";
}
