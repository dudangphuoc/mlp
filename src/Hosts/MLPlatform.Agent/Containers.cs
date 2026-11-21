using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using MLPlatform.AgentProtocol;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent;

/// <summary>Container ground truth (D-38): follows docker events for every mlp.managed=1 container (start / die) and
/// reports on the current channel.</summary>
public sealed class Containers(LogShipper logs, ILogger<Containers> log)
{
    private Func<Connection<ControlToAgent, AgentToControl>?> _conn = () => null;
    private int _watching;

    public void Bind(Func<Connection<ControlToAgent, AgentToControl>?> conn) { _conn = conn; logs.Bind(conn); }

    /// <summary>State of the platform's containers (running + exited).</summary>
    public static async Task<List<ContainerState>> ListAsync(CancellationToken ct, string? filterLabel = null)
    {
        var (code, ids, _) = await Docker.RunAsync(["ps", "-aq", "--filter", "label=" + (filterLabel ?? "mlp.managed=1")], ct);
        var res = new List<ContainerState>();
        if (code != 0 || ids.Length == 0) return res;
        var (ic, json, _) = await Docker.RunAsync(["inspect", .. ids.Split('\n', StringSplitOptions.RemoveEmptyEntries)], ct);
        if (ic != 0) return res;
        using var doc = JsonDocument.Parse(json);
        foreach (var c in doc.RootElement.EnumerateArray())
            if (FromInspect(c) is var state && HostOwnership.Owns(state.Labels)) res.Add(state);
        return res;
    }

    public static ContainerState FromInspect(JsonElement c)
    {
        var st = c.GetProperty("State");
        var s = new ContainerState
        {
            Name = c.GetProperty("Name").GetString()!.TrimStart('/'),
            State = st.GetProperty("Status").GetString() ?? "",
            ExitCode = st.GetProperty("ExitCode").GetInt32(),
            OomKilled = st.GetProperty("OOMKilled").GetBoolean(),
        };
        if (DateTimeOffset.TryParse(st.GetProperty("FinishedAt").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var f) && f.Year > 2000)
            s.FinishedAt = Timestamp.FromDateTimeOffset(f);
        if (c.GetProperty("Config").GetProperty("Labels") is { ValueKind: JsonValueKind.Object } labels)
            foreach (var l in labels.EnumerateObject()) s.Labels[l.Name] = l.Value.GetString() ?? "";
        s.SpecHash = s.Labels.GetValueOrDefault("mlp.spec", "");
        return s;
    }

    private static async Task<ContainerState?> InspectAsync(string name, CancellationToken ct)
    {
        var (code, json, _) = await Docker.RunAsync(["inspect", name], ct);
        if (code != 0) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetArrayLength() > 0 ? FromInspect(doc.RootElement[0]) : null;
    }

    /// <summary>Right after hello: follow logs of running containers (from ~1 minute back; the api deduplicates) + send
    /// a snapshot.</summary>
    public async Task OnConnectedAsync(Connection<ControlToAgent, AgentToControl> conn, CancellationToken ct)
    {
        await logs.FlushSpoolAsync(conn, ct);
        var all = await ListAsync(ct);
        foreach (var c in all.Where(c => c.State == "running" && c.Labels.ContainsKey("mlp.logs")))
            logs.Follow(c.Name, c.Labels["mlp.logs"], "1m");
        var snap = new ContainerSnapshot();
        snap.Containers.AddRange(all);
        await conn.SendAsync(new AgentToControl { ContainerSnapshot = snap }, ct);
        if (Interlocked.Exchange(ref _watching, 1) == 0) _ = Task.Run(() => WatchEventsAsync(CancellationToken.None), CancellationToken.None);
    }

    /// <summary><c>docker events</c> stream for the lifetime of the agent process (restarted if docker drops).</summary>
    private async Task WatchEventsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var psi = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
                foreach (var a in new[] { "events", "--format", "{{json .}}", "--filter", "type=container", "--filter", "label=mlp.managed=1",
                             "--filter", "event=start", "--filter", "event=die" }) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi)!;
                while (await p.StandardOutput.ReadLineAsync(ct) is { } line)
                {
                    using var ev = JsonDocument.Parse(line);
                    var action = ev.RootElement.GetProperty("Action").GetString() ?? "";
                    var name = ev.RootElement.GetProperty("Actor").GetProperty("Attributes").GetProperty("name").GetString() ?? "";
                    _ = Task.Run(() => OnEventAsync(name, action), CancellationToken.None);
                }
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception)
            {
                log.LogWarning("docker events stopped: {Msg}", e.Message);
            }
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
    }

    private async Task OnEventAsync(string name, string action)
    {
        var st = await InspectAsync(name, CancellationToken.None);
        if (st is null || !HostOwnership.Owns(st.Labels)) return;
        if (action == "start" && st.Labels.TryGetValue("mlp.logs", out var attempt)) logs.Follow(name, attempt);
        if (action == "die") await logs.DrainAsync(name, TimeSpan.FromSeconds(3));
        if (_conn() is not { } conn) return;
        try { await conn.SendAsync(new AgentToControl { ContainerEvent = new ContainerEvent { Container = st, Action = action } }); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
    }
}
