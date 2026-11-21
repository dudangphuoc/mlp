using Microsoft.Extensions.Logging;
using MLPlatform.AgentProtocol;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent;

/// <summary>Commands from the control plane (D-38): the agent is a container bridge with generic commands (container,
/// image, file, script) and no notion of Ray node / workload / network.</summary>
public sealed class CommandHandler(AgentState state, Containers containers, ILogger<CommandHandler> log)
{
    private Connection<ControlToAgent, AgentToControl>? _conn;

    public ValueTask HandleAsync(Connection<ControlToAgent, AgentToControl> conn, ControlToAgent msg, CancellationToken ct)
    {
        if (msg.BodyCase == ControlToAgent.BodyOneofCase.OpenDataChannel)
        {
            var open = msg.OpenDataChannel;
            _ = Task.Run(() => DataChannel.RunAsync(state.Options, open, log, ct), ct);
            return ValueTask.CompletedTask;
        }
        if (msg.BodyCase != ControlToAgent.BodyOneofCase.Command) return ValueTask.CompletedTask;
        var cmd = msg.Command;
        _ = Task.Run(() => RunCommandAsync(conn, msg, cmd, ct), ct);
        return ValueTask.CompletedTask;
    }

    private async Task RunCommandAsync(Connection<ControlToAgent, AgentToControl> conn, ControlToAgent msg, AgentCommand cmd,
        CancellationToken ct)
    {
        CommandResult result;
        var started = System.Diagnostics.Stopwatch.StartNew();
        var quiet = cmd.KindCase is AgentCommand.KindOneofCase.FileRead or AgentCommand.KindOneofCase.BrowsePath;
        if (!quiet) log.LogInformation("Command {Kind} started: {Detail}", cmd.KindCase, Describe(cmd));
        try { result = await ExecuteAsync(cmd, ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("Command {Kind} timed out", cmd.KindCase);
            result = new CommandResult { Ok = false, ErrorReason = "FLEET_COMMAND_TIMEOUT", Message = $"{cmd.KindCase} did not finish within its time limit" };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Command {Kind} failed", cmd.KindCase);
            result = new CommandResult { Ok = false, ErrorReason = "FLEET_COMMAND_FAILED", Message = e.Message };
        }
        result.CommandId = cmd.CommandId;
        if (!quiet)
        {
            if (result.Ok) log.LogInformation("Command {Kind} finished in {Seconds:0.0}s{Kept}", cmd.KindCase, started.Elapsed.TotalSeconds, result.Kept ? " (already present)" : "");
            else log.LogWarning("Command {Kind} failed after {Seconds:0.0}s: {Reason} {Message}", cmd.KindCase, started.Elapsed.TotalSeconds, result.ErrorReason, result.Message);
        }
        await conn.SendAsync(new AgentToControl { StreamId = msg.StreamId, CommandResult = result }, ct);
        if (cmd.KindCase == AgentCommand.KindOneofCase.Disconnect)
        {
            state.Disconnected = true;
            state.Forget();
            await conn.DisposeAsync();
        }
    }

    private static string Describe(AgentCommand c) => c.KindCase switch
    {
        AgentCommand.KindOneofCase.ImageBuild => $"{c.ImageBuild.Tag} ({c.ImageBuild.Files.Count} files, limit {(c.ImageBuild.TimeoutSeconds > 0 ? c.ImageBuild.TimeoutSeconds / 60 : 20)} min)",
        AgentCommand.KindOneofCase.ContainerApply => $"{c.ContainerApply.Spec.Name} ({c.ContainerApply.Spec.Image})",
        AgentCommand.KindOneofCase.ContainerRunOnce => c.ContainerRunOnce.Spec.Image,
        AgentCommand.KindOneofCase.ContainerStop => c.ContainerStop.Label,
        AgentCommand.KindOneofCase.ContainerRemove => c.ContainerRemove.Label,
        AgentCommand.KindOneofCase.PrepullImages => string.Join(", ", c.PrepullImages.Images),
        AgentCommand.KindOneofCase.RunScript => c.RunScript.Path,
        AgentCommand.KindOneofCase.FileWrite => c.FileWrite.Path,
        _ => "",
    };

    private Task<CommandResult> ExecuteAsync(AgentCommand cmd, CancellationToken ct) => cmd.KindCase switch
    {
        AgentCommand.KindOneofCase.BrowsePath => Task.FromResult(Browse(cmd.BrowsePath)),
        AgentCommand.KindOneofCase.Disconnect => Task.FromResult(new CommandResult { Ok = true }),
        AgentCommand.KindOneofCase.ContainerApply => ApplyAsync(cmd.ContainerApply, ct),
        AgentCommand.KindOneofCase.ContainerStop => StopAsync(cmd.ContainerStop, ct),
        AgentCommand.KindOneofCase.ContainerRemove => RemoveAsync(cmd.ContainerRemove, ct),
        AgentCommand.KindOneofCase.ContainerRunOnce => RunOnceAsync(cmd.ContainerRunOnce, ct),
        AgentCommand.KindOneofCase.ImageBuild => BuildAsync(cmd.ImageBuild, ct),
        AgentCommand.KindOneofCase.PrepullImages => PullAsync(cmd.PrepullImages, ct),
        AgentCommand.KindOneofCase.FileRead => HostFiles.ReadAsync(state.Dir, cmd.FileRead, ct),
        AgentCommand.KindOneofCase.FileWrite => HostFiles.WriteAsync(state.Dir, cmd.FileWrite, ct),
        AgentCommand.KindOneofCase.FileDelete => Task.FromResult(HostFiles.Delete(state.Dir, cmd.FileDelete)),
        AgentCommand.KindOneofCase.RunScript => HostFiles.RunScriptAsync(state.Dir, cmd.RunScript, ct),
        _ => Task.FromResult(new CommandResult
            { Ok = false, ErrorReason = "FLEET_COMMAND_UNSUPPORTED", Message = $"Agent {AgentService.Version} does not support {cmd.KindCase} yet" }),
    };

    private static CommandResult Fail(string err) => new() { Ok = false, ErrorReason = Docker.Reason(err), Message = err };

    /// <summary>Create a container from the spec; same name + same spec already running and keep_if_same → keep it (an
    /// agent reconnect does not lose running work).</summary>
    private async Task<CommandResult> ApplyAsync(ContainerApply a, CancellationToken ct)
    {
        var s = a.Spec;
        if (a.KeepIfSame && await Docker.RunAsync(["inspect", "-f", "{{.State.Running}} {{index .Config.Labels \"mlp.spec\"}}", s.Name], ct) is var (c, o, _)
            && c == 0 && o == "true " + Docker.SpecHash(s))
            return new CommandResult { Ok = true, Kept = true, Endpoint = await EndpointAsync(s, ct) };
        await Docker.RunAsync(["rm", "-f", s.Name], ct, TimeSpan.FromMinutes(1));
        var (code, _, err) = await Docker.RunAsync(Docker.RunArgs(s, once: false), ct, TimeSpan.FromSeconds(a.TimeoutSeconds > 0 ? a.TimeoutSeconds : 1800));
        if (code != 0) return Fail(err);
        log.LogInformation("Container {Name} ({Image})", s.Name, s.Image);
        return new CommandResult { Ok = true, Endpoint = await EndpointAsync(s, ct) };
    }

    private static async Task<string> EndpointAsync(ContainerSpec s, CancellationToken ct)
    {
        if (s.Ports.Count == 0) return "";
        var (pc, pout, _) = await Docker.RunAsync(["port", s.Name, s.Ports[0].ContainerPort.ToString(System.Globalization.CultureInfo.InvariantCulture)], ct);
        return pc == 0 ? pout.Split('\n')[0].Trim() : "";
    }

    private static async Task<string[]> TargetsAsync(string name, string label, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(name)) return [name];
        var (_, ids, _) = await Docker.RunAsync(["ps", "-aq", "--filter", "label=" + label], ct);
        return ids.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<CommandResult> StopAsync(ContainerStop s, CancellationToken ct)
    {
        var grace = (s.GraceSeconds > 0 ? s.GraceSeconds : 60).ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var id in await TargetsAsync(s.Name, s.Label, ct))
            await Docker.RunAsync(["stop", "-t", grace, id], ct, TimeSpan.FromSeconds(s.GraceSeconds + 120));
        return new CommandResult { Ok = true };
    }

    private static async Task<CommandResult> RemoveAsync(ContainerRemove r, CancellationToken ct)
    {
        foreach (var id in await TargetsAsync(r.Name, r.Label, ct))
            await Docker.RunAsync(r.Force ? ["rm", "-f", id] : ["rm", id], ct, TimeSpan.FromMinutes(2));
        foreach (var v in r.Volumes) await Docker.RunAsync(["volume", "rm", v], ct, TimeSpan.FromMinutes(1));
        return new CommandResult { Ok = true };
    }

    private static async Task<CommandResult> RunOnceAsync(ContainerRunOnce r, CancellationToken ct)
    {
        var (code, outp, err) = await Docker.RunAsync(Docker.RunArgs(r.Spec, once: true), ct, TimeSpan.FromSeconds(r.TimeoutSeconds > 0 ? r.TimeoutSeconds : 600));
        return new CommandResult { Ok = code == 0, ExitCode = code, Output = outp.Length > 65536 ? outp[^65536..] : outp,
            ErrorReason = code == 0 ? "" : Docker.Reason(err), Message = code == 0 ? "" : err };
    }

    private async Task<CommandResult> BuildAsync(ImageBuild b, CancellationToken ct)
    {
        if (!b.Force && (await Docker.RunAsync(["image", "inspect", b.Tag], ct)).Code == 0) return new CommandResult { Ok = true, Kept = true };
        var dir = Directory.CreateTempSubdirectory("mlp-build-");
        try
        {
            foreach (var (name, content) in b.Files)
            {
                if (HostFiles.Resolve(dir.FullName, name) is not { } p) return new CommandResult { Ok = false, ErrorReason = "FLEET_PATH_OUTSIDE_ROOT" };
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                await File.WriteAllBytesAsync(p, content.ToByteArray(), ct);
            }
            var limit = b.TimeoutSeconds > 0 ? TimeSpan.FromSeconds(b.TimeoutSeconds) : TimeSpan.FromMinutes(20);
            var lastProgress = -1L;
            var (code, tail) = await Docker.RunStreamingAsync(["build", "--progress=plain", "-t", b.Tag, dir.FullName], line =>
            {
                var step = System.Text.RegularExpressions.Regex.IsMatch(line, @"^#\d+ (\[|DONE|CACHED|ERROR)") || line.StartsWith("ERROR", StringComparison.Ordinal);
                var progress = !step && System.Text.RegularExpressions.Regex.IsMatch(line, @"^#\d+ (sha256|extracting|Downloading|Collecting|Installing)");
                if (progress && lastProgress >= 0 && System.Diagnostics.Stopwatch.GetElapsedTime(lastProgress) < TimeSpan.FromSeconds(15)) return;
                if (progress) lastProgress = System.Diagnostics.Stopwatch.GetTimestamp();
                if (step || progress) log.LogInformation("build {Tag}: {Line}", b.Tag, line.Length > 300 ? line[..300] + "…" : line);
            }, ct, limit);
            return code == 0 ? new CommandResult { Ok = true } : new CommandResult { Ok = false, ErrorReason = "FLEET_IMAGE_BUILD_FAILED", Message = tail };
        }
        finally { dir.Delete(true); }
    }

    private static async Task<CommandResult> PullAsync(PrepullImages p, CancellationToken ct)
    {
        foreach (var img in p.Images)
        {
            var (code, _, err) = await Docker.RunAsync(["pull", "-q", img], ct, TimeSpan.FromMinutes(60));
            if (code != 0) return new CommandResult { Ok = false, ErrorReason = "FLEET_IMAGE_PULL_FAILED", Message = err };
        }
        return new CommandResult { Ok = true };
    }

    /// <summary>List only inside data_path; block escaping outside it (DATA_PATH_OUTSIDE_ROOT).</summary>
    private CommandResult Browse(BrowsePath b)
    {
        var root = Path.GetFullPath(state.Options.DataPath);
        var target = Path.GetFullPath(Path.Combine(root, b.RelativePath.TrimStart('/', '\\')));
        if (!target.StartsWith(root, StringComparison.Ordinal))
            return new CommandResult { Ok = false, ErrorReason = "DATA_PATH_OUTSIDE_ROOT", Message = "Path is outside the data folder" };
        if (!Directory.Exists(target))
            return new CommandResult { Ok = false, ErrorReason = "DATA_PATH_NOT_FOUND", Message = "Folder does not exist" };
        var res = new BrowsePathResult();
        var dir = new DirectoryInfo(target);
        foreach (var d in dir.EnumerateDirectories().OrderBy(x => x.Name).Take(1000))
            res.Entries.Add(new PathEntry { Name = d.Name, IsDir = true });
        foreach (var f in dir.EnumerateFiles().OrderBy(x => x.Name).Take(1000))
            res.Entries.Add(new PathEntry { Name = f.Name, IsDir = false, Size = (ulong)f.Length });
        return new CommandResult { Ok = true, BrowsePath = res };
    }

    /// <summary>Right after hello: attach the new channel to the event + log streams, send a container snapshot.</summary>
    public async Task OnConnectedAsync(Connection<ControlToAgent, AgentToControl> conn, CancellationToken ct)
    {
        _conn = conn;
        containers.Bind(() => _conn);
        await containers.OnConnectedAsync(conn, ct);
    }
}
