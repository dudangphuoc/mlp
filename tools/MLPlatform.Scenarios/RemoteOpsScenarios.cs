using System.Net.WebSockets;
using Grpc.Core;
using Grpc.Net.Client;
using MLPlatform.Contracts.V1;

/// <summary>Remote ops on a connected agent: files, transfers, processes, terminal, permissions, audit
/// (docs/agent-upgrade.md §1).</summary>
public static class RemoteOpsScenarios
{
    public sealed record Context(GrpcChannel Channel, Metadata Auth, string Token, string WorkspaceId, string HostId, string DataDir, string WebUrl,
        Func<string, Func<Task>, Task> Step);

    public static async Task RunAsync(Context c)
    {
        var fleet = new FleetService.FleetServiceClient(c.Channel);
        await c.Step("Ops: allowed roots, list/write/read/move/delete inside data, outside refused", () => FilesAsync(c, fleet));
        await c.Step("Ops: upload + download over the data channel, sha256 matches", () => TransferAsync(c));
        await c.Step("Ops: process list, kill a scenario process, PID 1 refused", () => ProcessesAsync(c, fleet));
        await c.Step("Ops: terminal WebSocket echo, resize, exit code, audited open/close", () => TerminalAsync(c));
        await c.Step("Ops: services and system log answer (or report unavailable)", () => ServicesAsync(c, fleet));
        await c.Step("Ops: an admin member reads but cannot write files, kill processes or open a terminal", () => RolesAsync(c, fleet));
        await c.Step("Ops: audit records carry path, sha256 and terminal details; chain valid", () => AuditAsync(c));
    }

    private static void Assert(bool cond, string msg) { if (!cond) throw new Exception(msg); }

    private static async Task ExpectReason(string reason, Func<Task> call)
    {
        try { await call(); }
        catch (RpcException e) { Assert(e.Trailers.GetValue("mlp-error-reason") == reason, $"expected {reason}, got {e.StatusCode} {e.Trailers.GetValue("mlp-error-reason")}"); return; }
        throw new Exception($"expected {reason} but the call succeeded");
    }

    private static async Task FilesAsync(Context c, FleetService.FleetServiceClient fleet)
    {
        var roots = await fleet.ListHostFilesAsync(new HostPathRequest { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Path = "" }, c.Auth);
        var data = roots.Entries.Select(e => e.Name).FirstOrDefault(n => n.EndsWith(Path.GetFileName(c.DataDir), StringComparison.Ordinal))
                   ?? throw new Exception("data folder missing from roots: " + string.Join(", ", roots.Entries.Select(e => e.Name)));
        var file = Path.Combine(data, "ops", "hello.txt");
        var w = await fleet.WriteHostFileAsync(new WriteHostFileRequest { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Path = file, Truncate = true,
            Data = Google.Protobuf.ByteString.CopyFromUtf8("hello mlp\n") }, c.Auth);
        Assert(w.Sha256.Length == 64 && w.Size == 10, "write stat wrong");
        var r = await fleet.ReadHostFileAsync(new ReadHostFileRequest { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Path = file }, c.Auth);
        Assert(r.Data.ToStringUtf8() == "hello mlp\n" && r.Stat.IsText, "read back differs");
        var moved = Path.Combine(data, "ops", "moved.txt");
        await fleet.MoveHostPathAsync(new MoveHostPathRequest { WorkspaceId = c.WorkspaceId, HostId = c.HostId, From = file, To = moved }, c.Auth);
        var ls = await fleet.ListHostFilesAsync(new HostPathRequest { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Path = Path.Combine(data, "ops") }, c.Auth);
        Assert(ls.Entries.Any(e => e.Name == "moved.txt") && ls.Entries.All(e => e.Name != "hello.txt"), "move not visible");
        await fleet.DeleteHostPathAsync(new DeleteHostPathRequest { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Path = Path.Combine(data, "ops"), Recursive = true }, c.Auth);
        await ExpectReason("FLEET_PATH_OUTSIDE_ROOT", async () => await fleet.ReadHostFileAsync(new ReadHostFileRequest
            { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Path = "/etc/hosts" }, c.Auth));
        await ExpectReason("FLEET_PATH_OUTSIDE_ROOT", async () => await fleet.WriteHostFileAsync(new WriteHostFileRequest
            { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Path = "/etc/mlp-escape.txt", Truncate = true }, c.Auth));
        await ExpectReason("FLEET_PATH_OUTSIDE_ROOT", async () => await fleet.ListHostFilesAsync(new HostPathRequest
            { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Path = "../.." }, c.Auth));
        await ExpectReason("FLEET_PATH_OUTSIDE_ROOT", async () => await fleet.DeleteHostPathAsync(new DeleteHostPathRequest
            { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Path = data, Recursive = true }, c.Auth));
    }

    private static HttpClient Http(Context c)
    {
        var h = new HttpClient { BaseAddress = new Uri(c.WebUrl), Timeout = TimeSpan.FromMinutes(2) };
        h.DefaultRequestHeaders.Authorization = new("Bearer", c.Token);
        return h;
    }

    private static async Task TransferAsync(Context c)
    {
        using var h = Http(c);
        var payload = new byte[3 * 1024 * 1024 + 17];
        Random.Shared.NextBytes(payload);
        var path = Path.Combine(c.DataDir, "transfer.bin");
        var q = $"/api/hosts/{c.HostId}/files?workspace_id={c.WorkspaceId}&path={Uri.EscapeDataString(path)}";
        var put = await h.PutAsync(q, new ByteArrayContent(payload));
        Assert(put.IsSuccessStatusCode, $"upload HTTP {(int)put.StatusCode} {(put.Headers.TryGetValues("x-mlp-reason", out var why) ? why.First() : "")}");
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload));
        Assert((await put.Content.ReadAsStringAsync()).Contains(sha), "upload sha256 differs");
        var got = await h.GetByteArrayAsync(q);
        Assert(got.AsSpan().SequenceEqual(payload), "download differs");
        var outside = await h.GetAsync($"/api/hosts/{c.HostId}/files?workspace_id={c.WorkspaceId}&path={Uri.EscapeDataString("/etc/hosts")}");
        Assert((int)outside.StatusCode == 403, "download outside the roots must be 403, got " + (int)outside.StatusCode);
    }

    private static async Task ProcessesAsync(Context c, FleetService.FleetServiceClient fleet)
    {
        using var sleeper = System.Diagnostics.Process.Start("sleep", "300")!;
        var list = await fleet.ListHostProcessesAsync(new ListHostProcessesRequest { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Sort = "memory" }, c.Auth);
        Assert(list.Total > 5 && list.Processes.Count > 0, "process list empty");
        await fleet.KillHostProcessAsync(new KillHostProcessRequest { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Pid = sleeper.Id }, c.Auth);
        Assert(sleeper.WaitForExit(10_000), "sleep process still running after kill");
        await ExpectReason("FLEET_PROCESS_PROTECTED", async () => await fleet.KillHostProcessAsync(new KillHostProcessRequest
            { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Pid = 1, Force = true }, c.Auth));
    }

    private static async Task TerminalAsync(Context c)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + c.Token);
        var ws = new Uri(c.WebUrl.Replace("http", "ws") + $"/api/hosts/{c.HostId}/terminal?workspace_id={c.WorkspaceId}&reason=scenario&cols=100&rows=30");
        await socket.ConnectAsync(ws, CancellationToken.None);
        await WaitForOutputAsync(socket, TimeSpan.FromSeconds(15));
        await SendAsync(socket, 1, [0, 120, 0, 40]);
        await SendAsync(socket, 0, System.Text.Encoding.UTF8.GetBytes("echo mlp-$((40+2))-ok; exit 7\n"));
        var (output, exit) = await ReadUntilExitAsync(socket, TimeSpan.FromSeconds(30));
        Assert(output.Contains("mlp-42-ok"), "terminal output missing the echo: " + (output.Length > 300 ? output[^300..] : output));
        Assert(exit == 7, "exit code " + exit);
        using var noReason = new ClientWebSocket();
        noReason.Options.SetRequestHeader("Authorization", "Bearer " + c.Token);
        var refused = false;
        try { await noReason.ConnectAsync(new Uri(ws.ToString().Replace("reason=scenario", "reason=")), CancellationToken.None); }
        catch (WebSocketException) { refused = true; }
        Assert(refused, "a terminal without a reason must be refused");
    }

    /// <summary>Waits for the shell's first output (prompt) so typed input is not flushed while the terminal starts.</summary>
    private static async Task WaitForOutputAsync(ClientWebSocket s, TimeSpan limit)
    {
        using var cts = new CancellationTokenSource(limit);
        var buf = new byte[70_000];
        var r = await s.ReceiveAsync(buf, cts.Token);
        Assert(r.MessageType == WebSocketMessageType.Binary && buf[0] == 0, "terminal sent no output");
        await Task.Delay(300);
    }

    private static Task SendAsync(ClientWebSocket s, byte kind, byte[] payload) =>
        s.SendAsync((byte[])[kind, .. payload], WebSocketMessageType.Binary, true, CancellationToken.None);

    private static async Task<(string Output, int? Exit)> ReadUntilExitAsync(ClientWebSocket s, TimeSpan limit)
    {
        using var cts = new CancellationTokenSource(limit);
        var text = new System.Text.StringBuilder();
        var buf = new byte[70_000];
        try
        {
            while (s.State == WebSocketState.Open)
            {
                var r = await s.ReceiveAsync(buf, cts.Token);
                if (r.MessageType == WebSocketMessageType.Close || r.Count == 0) break;
                if (buf[0] == 0) text.Append(System.Text.Encoding.UTF8.GetString(buf, 1, r.Count - 1));
                if (buf[0] == 2) return (text.ToString(), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(buf.AsSpan(1)));
            }
        }
        catch (OperationCanceledException) { }
        return (text.ToString(), null);
    }

    private static async Task ServicesAsync(Context c, FleetService.FleetServiceClient fleet)
    {
        try
        {
            var s = await fleet.ListHostServicesAsync(new GetHostRequest { WorkspaceId = c.WorkspaceId, HostId = c.HostId }, c.Auth);
            Assert(s.Manager.Length > 0, "no service manager");
        }
        catch (RpcException e) when (e.Trailers.GetValue("mlp-error-reason") == "FLEET_SERVICES_UNAVAILABLE") { }
        await ExpectReason("FLEET_SERVICE_PROTECTED", async () => await fleet.ControlHostServiceAsync(new ControlHostServiceRequest
            { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Name = "mlplatform-agent", Action = "stop" }, c.Auth));
    }

    private static async Task RolesAsync(Context c, FleetService.FleetServiceClient fleet)
    {
        var iam = new IamService.IamServiceClient(c.Channel);
        var wsc = new WorkspaceService.WorkspaceServiceClient(c.Channel);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var inv = await wsc.CreateInvitationAsync(new CreateInvitationRequest { WorkspaceId = c.WorkspaceId, Email = $"opsadm{tag}@x.io", Role = "admin" }, c.Auth);
        await iam.RegisterAsync(new RegisterRequest { Email = $"opsadm{tag}@x.io", Password = "mot-mat-khau-dai", Username = $"opsadm{tag}", InvitationToken = inv.Token });
        var token = (await iam.LoginAsync(new LoginRequest { Email = $"opsadm{tag}@x.io", Password = "mot-mat-khau-dai" })).SessionToken;
        var admin = new Metadata { { "authorization", "Bearer " + token } };
        var roots = await fleet.ListHostFilesAsync(new HostPathRequest { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Path = "" }, admin);
        Assert(roots.Entries.Count > 0, "admin cannot list files");
        await ExpectReason("IAM_PERMISSION_DENIED", async () => await fleet.WriteHostFileAsync(new WriteHostFileRequest
            { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Path = Path.Combine(c.DataDir, "x.txt"), Truncate = true }, admin));
        await ExpectReason("IAM_PERMISSION_DENIED", async () => await fleet.KillHostProcessAsync(new KillHostProcessRequest
            { WorkspaceId = c.WorkspaceId, HostId = c.HostId, Pid = 999999 }, admin));
        using var h = new HttpClient { BaseAddress = new Uri(c.WebUrl) };
        h.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var put = await h.PutAsync($"/api/hosts/{c.HostId}/files?workspace_id={c.WorkspaceId}&path={Uri.EscapeDataString(Path.Combine(c.DataDir, "x.txt"))}", new ByteArrayContent([1]));
        Assert((int)put.StatusCode == 403, "admin upload must be 403, got " + (int)put.StatusCode);
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        var refused = false;
        try { await socket.ConnectAsync(new Uri(c.WebUrl.Replace("http", "ws") + $"/api/hosts/{c.HostId}/terminal?workspace_id={c.WorkspaceId}&reason=try"), CancellationToken.None); }
        catch (WebSocketException) { refused = true; }
        Assert(refused, "admin must not open a terminal");
    }

    private static async Task AuditAsync(Context c)
    {
        var audit = new AuditService.AuditServiceClient(c.Channel);
        AuditEvent[] events = [];
        for (var i = 0; i < 20; i++)
        {
            var ev = await audit.ListEventsAsync(new ListAuditEventsRequest { WorkspaceId = c.WorkspaceId }, c.Auth);
            Assert(ev.ChainValid, "audit hash chain is broken");
            events = [.. ev.Events];
            if (events.Any(e => e.Action == "CloseTerminalSessionCommand")) break;
            await Task.Delay(500);
        }
        Assert(events.Any(e => e.Action == "WriteHostFileCommand" && e.Details.Contains("\"sha256\"") && e.Details.Contains("hello.txt")), "file write audit missing details");
        Assert(events.Any(e => e.Action == "UploadHostFileCommand" && e.Details.Contains("transfer.bin")), "upload audit missing");
        Assert(events.Any(e => e.Action == "KillHostProcessCommand" && e.Outcome == "ok"), "kill audit missing");
        Assert(events.Any(e => e.Action == "OpenTerminalSessionCommand" && e.Outcome == "ok" && e.Details.Contains("\"reason\":\"scenario\"")), "terminal open audit missing");
        Assert(events.Any(e => e.Action == "OpenTerminalSessionCommand" && e.Outcome == "failed"), "refused terminal (no reason) not audited");
        Assert(events.Any(e => e.Action == "CloseTerminalSessionCommand" && e.Details.Contains("\"exit_code\":7")), "terminal close audit missing exit code");
    }
}
