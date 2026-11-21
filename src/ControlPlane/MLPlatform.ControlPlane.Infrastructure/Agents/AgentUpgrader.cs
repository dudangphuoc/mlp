using Google.Protobuf;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>
/// Agent OTA (D-38, blogic-tunnels pattern): the control plane decides and orchestrates; the agent only offers generic operations (FileWrite, RunScript
/// detached from itself, FileRead). Steps: build the right RID (agent-builder) → generate a script (sh/ps1) with a 1-hour signed URL + SHA-256 → the agent runs
/// the script via the service manager → read status.txt every 3 s → once the new agent has been connected ≥ 30 s, write final-ok → the script cleans up and reports done;
/// without final-ok within 240 s the script restores the old version on its own. Running containers are not touched (the agent is only a bridge).
/// At most 4 hosts at a time; jobs are stored in the DB (tracking resumes after an api restart).
/// </summary>
public sealed class AgentUpgrader(AgentRegistry agents, AgentBuildStore builds, IServiceScopeFactory scopes, IConfiguration cfg, TimeProvider time,
    SystemHealth health, ILogger<AgentUpgrader> log) : BackgroundService
{
    private readonly SemaphoreSlim _slots = new(4);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Task> _running = new();
    private static readonly string ShTemplate = Resource("agent-upgrade/upgrade.sh");
    private static readonly string PsTemplate = Resource("agent-upgrade/upgrade.ps1");

    private static string Resource(string name)
    {
        using var s = typeof(AgentUpgrader).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("missing " + name);
        using var r = new StreamReader(s);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }

    public string? LatestVersion => builds.Version;

    /// <summary>Upgrade request (RPC): host offline / already on the version (unless forced) → job skipped with a reason.</summary>
    public async Task<IReadOnlyList<Guid>> RequestAsync(IMediator m, Guid workspaceId, IReadOnlyList<Guid> hostIds, bool force, CancellationToken ct)
    {
        var to = builds.Version ?? throw new MLPlatform.Cqrs.PreconditionException("FLEET_AGENT_BUILDER_MISSING", "The control plane has no agent-builder yet");
        var targets = hostIds.Count > 0 ? hostIds : agents.All.Where(a => a.WorkspaceId == workspaceId && a.AgentVersion != to).Select(a => a.HostId).ToList();
        var ids = new List<Guid>();
        foreach (var h in targets)
        {
            agents.TryGet(h, out var s);
            var from = s?.AgentVersion ?? "";
            string? skip = s is null ? "Host is offline" : s.WorkspaceId != workspaceId ? "Host does not belong to this workspace"
                : from == to && !force ? "Already on this version" : null;
            var id = await m.Send(new RequestAgentUpgradeCommand(workspaceId, h, from, to, skip is not null, skip ?? ""), ct);
            ids.Add(id);
            if (skip is null) Start(id, h, to);
        }
        return ids;
    }

    private void Start(Guid job, Guid hostId, string to) =>
        _running.TryAdd(job, Task.Run(async () =>
        {
            await _slots.WaitAsync();
            try { await RunAsync(job, hostId, to, CancellationToken.None); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Agent upgrade {Job} failed", job);
                await UpdateAsync(job, "failed", "", e.Message);
            }
            finally { _slots.Release(); _running.TryRemove(job, out _); }
        }));

    private async Task UpdateAsync(Guid job, string state, string phase, string message)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new UpdateAgentUpgradeCommand(job, state, phase, message));
    }

    private static string Dir(Guid job) => $"upgrade/{job:N}";

    private async Task RunAsync(Guid job, Guid hostId, string to, CancellationToken ct)
    {
        if (!agents.TryGet(hostId, out var s) || s.Info is not { } info) { await UpdateAsync(job, "failed", "", "Host offline"); return; }
        var rid = info.AgentRid;
        if (!AgentBuildStore.Supported.Contains(rid)) { await UpdateAsync(job, "failed", "", $"Platform {rid} does not support OTA yet"); return; }
        if (info.ServiceManager is not ("systemd" or "launchd" or "schtasks"))
        { await UpdateAsync(job, "failed", "", "The agent is not running under a service manager; reinstall it with the install command to enable OTA"); return; }
        await UpdateAsync(job, "running", "building", $"Build agent {rid}");
        var st = await builds.EnsureAsync(to, rid, TimeSpan.FromMinutes(20), ct);
        if (st["status"]!.ToString() != "ready") { await UpdateAsync(job, "failed", "building", "Agent build failed: " + st["log"]); return; }
        var url = (cfg["Install:DownloadBase"] ?? cfg["PublicUrl"] ?? "").TrimEnd('/') + builds.SignedPath(to, rid, TimeSpan.FromHours(1));
        var win = rid.StartsWith("win", StringComparison.Ordinal);
        var script = (win ? PsTemplate : ShTemplate).Replace("__EXE__", info.AgentExe).Replace("__URL__", url)
            .Replace("__SHA__", st["sha256"]!.ToString()).Replace("__SVC__", info.ServiceManager);
        var path = $"{Dir(job)}/upgrade.{(win ? "ps1" : "sh")}";
        // PowerShell 5.1 reads a BOM-less .ps1 using the ANSI code page → non-ASCII text is garbled: write it with a UTF-8 BOM.
        var bytes = win ? [.. System.Text.Encoding.UTF8.GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(script)] : System.Text.Encoding.UTF8.GetBytes(script);
        var w = await s.SendCommandAsync(new AgentCommand { FileWrite = new FileWrite { Path = path, Content = ByteString.CopyFrom(bytes), Executable = !win } }, TimeSpan.FromMinutes(1), ct);
        if (!w.Ok) { await UpdateAsync(job, "failed", "", "Could not write the script: " + w.Message); return; }
        var r = await s.SendCommandAsync(new AgentCommand { RunScript = new RunScript
            { Path = path, Shell = win ? "powershell" : "sh", Detached = true, JobName = "mlp-agent-upgrade-" + job.ToString("N")[..8] } }, TimeSpan.FromMinutes(2), ct);
        if (!r.Ok) { await UpdateAsync(job, "failed", "", "Could not run the script: " + r.Message); return; }
        log.LogInformation("Agent upgrade {Host} → {Ver} ({Rid}) started", hostId, to, rid);
        await MonitorAsync(job, hostId, to, ct);
    }

    /// <summary>Watch status.txt; confirm the new version; finalize the job. Also used when the api restarts midway.</summary>
    private async Task MonitorAsync(Guid job, Guid hostId, string to, CancellationToken ct)
    {
        var deadline = time.GetUtcNow().AddMinutes(25);
        var okWritten = false;
        var lastPhase = "";
        while (time.GetUtcNow() < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            if (!agents.TryGet(hostId, out var s)) continue;   // agent is restarting
            try
            {
                if (!okWritten && s.AgentVersion == to && time.GetUtcNow() - s.ConnectedAt >= TimeSpan.FromSeconds(30))
                {
                    var ok = await s.SendCommandAsync(new AgentCommand { FileWrite = new FileWrite { Path = $"{Dir(job)}/final-ok", Content = ByteString.CopyFromUtf8("ok") } }, TimeSpan.FromSeconds(30), ct);
                    okWritten = ok.Ok;
                }
                var f = await s.SendCommandAsync(new AgentCommand { FileRead = new FileRead { Path = $"{Dir(job)}/status.txt", MaxBytes = 8192 } }, TimeSpan.FromSeconds(30), ct);
                if (!f.Ok || !f.Exists) continue;
                var last = f.Data.ToStringUtf8().Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                var sp = last.IndexOf(' ');
                var text = (sp > 0 ? last[(sp + 1)..] : last).Trim().TrimStart('\uFEFF');   // drop the timestamp
                if (text.StartsWith("failed", StringComparison.Ordinal) || text.StartsWith("rolled back", StringComparison.Ordinal))
                {
                    await UpdateAsync(job, "failed", text.StartsWith("rolled", StringComparison.Ordinal) ? "rolled back" : lastPhase, text);
                    log.LogWarning("Agent upgrade {Host} failed: {Msg}", hostId, text);
                    return;
                }
                if (text == "done")
                {
                    await UpdateAsync(job, "succeeded", "done", $"Agent {to}");
                    await s.SendCommandAsync(new AgentCommand { FileDelete = new FileDelete { Path = Dir(job), Recursive = true } }, TimeSpan.FromSeconds(30), ct);
                    log.LogInformation("Agent upgrade {Host} → {Ver} done", hostId, to);
                    return;
                }
                if (text != lastPhase) { lastPhase = text; await UpdateAsync(job, "running", text, ""); }
            }
            catch (Exception e) when (e is TimeoutException or IOException or InvalidOperationException or ObjectDisposedException) { }
        }
        await UpdateAsync(job, "failed", lastPhase, "Not finished after 25 minutes");
    }

    /// <summary>api restarted: resume tracking unfinished jobs.</summary>
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stop);
        await using (var scope = scopes.CreateAsyncScope())
            foreach (var j in await scope.ServiceProvider.GetRequiredService<IMediator>().Query(new ActiveAgentUpgradesQuery(), stop))
                if (!_running.ContainsKey(j.Id))
                    _running.TryAdd(j.Id, Task.Run(() => MonitorAsync(j.Id, j.HostId, j.ToVersion, stop), stop));
        health.Beat("agent-upgrade", 0);
    }
}
