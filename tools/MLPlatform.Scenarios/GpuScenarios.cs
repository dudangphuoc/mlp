using Grpc.Core;
using Grpc.Net.Client;
using MLPlatform.Contracts.V1;

/// <summary>GPU monitoring and prod/dev GPU priority with two agents on one Docker daemon and a fake nvidia-smi
/// (docs/agent-upgrade.md §2, §3).</summary>
public static class GpuScenarios
{
    public sealed record Context(GrpcChannel Channel, Metadata Auth, string WorkspaceId, Func<string, Func<Task>, Task> Step,
        Func<string, string[], Dictionary<string, string>, System.Diagnostics.Process> StartAgent, string SmallImage, string? RayImage, string Network);

    private static void Assert(bool cond, string msg) { if (!cond) throw new Exception(msg); }

    public static async Task RunAsync(Context c)
    {
        var fleet = new FleetService.FleetServiceClient(c.Channel);
        var dir = Directory.CreateTempSubdirectory("mlp-gpu-");
        var smi = FakeSmi();
        var failFlag = Path.Combine(dir.FullName, "driver-broken");
        var env = new Dictionary<string, string> { ["MLP_NVIDIA_SMI"] = smi, ["MLP_FAKE_GPU"] = "1", ["MLP_FAKE_GPU_TEMP"] = "95", ["MLP_FAKE_GPU_FAIL"] = failFlag };
        string prodId = "", devId = "", foreign = $"mlp-fake-prod-{Guid.NewGuid():N}"[..24];
        var agents = new List<System.Diagnostics.Process>();
        try
        {
            await c.Step("GPU: two agents on one daemon (prod normal, dev yield) come online with the fake GPU", async () =>
            {
                prodId = await EnrollAsync(c, fleet, dir, "prod", [], env, agents);
                devId = await EnrollAsync(c, fleet, dir, "dev", ["--gpu-priority", "yield"], env, agents);
            });
            await c.Step("GPU: per-GPU readings, history points and a hot alert", () => MonitoringAsync(c, fleet, prodId));
            await c.Step("GPU: a prod GPU container makes the dev agent yield; it clears when the container goes", () => YieldAsync(c, fleet, prodId, devId, foreign));
            if (c.RayImage is not null)
                await c.Step("GPU: a dev job pinned to the yielding host waits (gpu_yielded) without spending attempts, then runs", () => YieldedJobAsync(c, prodId, devId, foreign));
            await c.Step("GPU: a broken driver reads as driver_error with a lost alert", () => DriverErrorAsync(c, fleet, prodId, failFlag));
        }
        finally
        {
            await Docker("rm", "-f", foreign);
            foreach (var a in agents.Where(a => !a.HasExited)) a.Kill();
        }
    }

    private static string FakeSmi()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Path.Combine(d.FullName, "fixtures", "fake-gpu", "nvidia-smi") is var p && File.Exists(p)) return p;
        throw new FileNotFoundException("fixtures/fake-gpu/nvidia-smi not found above " + AppContext.BaseDirectory);
    }

    private static async Task<string> EnrollAsync(Context c, FleetService.FleetServiceClient fleet, DirectoryInfo dir, string name, string[] extra,
        Dictionary<string, string> env, List<System.Diagnostics.Process> agents)
    {
        var data = Directory.CreateDirectory(Path.Combine(dir.FullName, name)).FullName;
        var e = await fleet.CreateEnrollmentAsync(new CreateEnrollmentRequest { WorkspaceId = c.WorkspaceId, Os = OsFamily.Macos, DataPath = data, ModelsPath = data }, c.Auth);
        agents.Add(c.StartAgent(Path.Combine(data, "agent.json"), [.. extra, "--token", e.Token, "--data", data, "--models", data], env));
        for (var i = 0; i < 60; i++)
        {
            var hosts = await fleet.ListHostsAsync(new ListHostsRequest { WorkspaceId = c.WorkspaceId }, c.Auth);
            if (hosts.Hosts.FirstOrDefault(h => h.State == "online" && h.HostInfo?.DataPath == data && h.HostInfo.Gpus.Count == 1) is { } h) return h.Id;
            await Task.Delay(500);
        }
        throw new Exception($"{name} agent not online with one GPU within 30 s");
    }

    private static async Task<HostGpuView> WaitView(Context c, FleetService.FleetServiceClient fleet, string hostId, Func<HostGpuView, bool> ok, int seconds = 45)
    {
        HostGpuView v = new();
        for (var i = 0; i < seconds; i++)
        {
            v = await fleet.GetHostGpusAsync(new GetHostRequest { WorkspaceId = c.WorkspaceId, HostId = hostId }, c.Auth);
            if (ok(v)) return v;
            await Task.Delay(1000);
        }
        return v;
    }

    private static async Task MonitoringAsync(Context c, FleetService.FleetServiceClient fleet, string prodId)
    {
        var v = await WaitView(c, fleet, prodId, v => v.State == GpuState.Ok && v.Gpus.Count == 1 && v.Gpus[0].Sample.MemoryTotalBytes > 0);
        Assert(v.State == GpuState.Ok && v.Gpus.Count == 1, $"state={v.State} gpus={v.Gpus.Count}");
        var s = v.Gpus[0].Sample;
        Assert(s.UtilPercent == 37 && s.TemperatureC == 95 && s.PowerLimitW == 450 && s.SmClockMhz == 2100 && !s.HasEccUncorrected && s.TemperatureLimitC == 99,
            $"wrong sample: {s}");
        Assert(v.Gpus[0].Alerts.Any(a => a.Kind == "hot" && a.Level == "warn"), "hot alert missing: " + string.Join(",", v.Gpus[0].Alerts.Select(a => a.Kind + ":" + a.Level)));
        var h = await fleet.GetGpuHistoryAsync(new GetGpuHistoryRequest { WorkspaceId = c.WorkspaceId, HostId = prodId, GpuUuid = s.Uuid, Range = "1h" }, c.Auth);
        Assert(h.Points.Count >= 1 && h.MemoryTotalBytes == 24576UL << 20, $"history points={h.Points.Count} total={h.MemoryTotalBytes}");
    }

    private static async Task YieldAsync(Context c, FleetService.FleetServiceClient fleet, string prodId, string devId, string foreign)
    {
        var (code, err) = await Docker("run", "-d", "--name", foreign, "--label", "mlp.managed=1", "--label", "mlp.agent=" + prodId,
            "--label", "mlp.gpus=GPU-fake-0000", c.SmallImage, "sleep", "600");
        Assert(code == 0, "could not start the fake prod GPU container: " + err);
        var dev = await WaitView(c, fleet, devId, v => v.Yield?.Active == true);
        Assert(dev.Yield?.Active == true && dev.Yield.ForeignContainers.Contains(foreign), "dev agent did not yield: " + dev.Yield);
        Assert(dev.GpuPriority == "yield", "dev gpu_priority " + dev.GpuPriority);
        var prod = await fleet.GetHostGpusAsync(new GetHostRequest { WorkspaceId = c.WorkspaceId, HostId = prodId }, c.Auth);
        Assert(prod.Yield?.Active != true, "prod must never yield");
        Assert(dev.Usage.Any(u => u.Container == foreign && u.Environment == "other" && u.Estimated), "dev usage does not show the prod container as other");
        await Docker("rm", "-f", foreign);
        dev = await WaitView(c, fleet, devId, v => v.Yield?.Active != true);
        Assert(dev.Yield?.Active != true, "dev still yields after the prod container is gone");
    }

    private static async Task YieldedJobAsync(Context c, string prodId, string devId, string foreign)
    {
        var sched = new SchedulerService.SchedulerServiceClient(c.Channel);
        var (code, err) = await Docker("run", "-d", "--name", foreign, "--label", "mlp.managed=1", "--label", "mlp.agent=" + prodId,
            "--label", "mlp.gpus=GPU-fake-0000", c.SmallImage, "sleep", "600");
        Assert(code == 0, "could not start the fake prod GPU container: " + err);
        var req = new SubmitJobRequest { WorkspaceId = c.WorkspaceId, Kind = "train", Name = "gpu-yield", Image = c.RayImage!, Cpus = 1, Gpus = 1, Nnodes = 1, Network = c.Network };
        req.HostIds.Add(devId);
        req.Command.AddRange(["python", "-c", "print('gpu job ran')"]);
        var job = await sched.SubmitJobAsync(req, c.Auth);
        var j = await WaitJob(sched, c, job.Id, j => j.StateReason == "gpu_yielded", 240);
        Assert(j.StateReason == "gpu_yielded" && j.State is "queued" or "scheduling", $"job did not wait for the GPU: state={j.State} reason={j.StateReason}");
        await Docker("rm", "-f", foreign);
        j = await WaitJob(sched, c, job.Id, j => j.State is "succeeded" or "failed" or "cancelled", 300);
        Assert(j.State == "succeeded", $"state={j.State} reason={j.StateReason} exit={j.LastExitReason}");
        Assert(j.AttemptCount == 1, "yielded attempts were counted: attempt_count=" + j.AttemptCount);
    }

    private static async Task<Job> WaitJob(SchedulerService.SchedulerServiceClient sched, Context c, string id, Func<Job, bool> done, int seconds)
    {
        Job j = new();
        for (var i = 0; i < seconds; i++)
        {
            j = await sched.GetJobAsync(new GetJobRequest { WorkspaceId = c.WorkspaceId, JobId = id }, c.Auth);
            if (done(j)) return j;
            await Task.Delay(1000);
        }
        return j;
    }

    private static async Task DriverErrorAsync(Context c, FleetService.FleetServiceClient fleet, string prodId, string failFlag)
    {
        await File.WriteAllTextAsync(failFlag, "x");
        try
        {
            var v = await WaitView(c, fleet, prodId, v => v.State == GpuState.DriverError);
            Assert(v.State == GpuState.DriverError && v.Error.Contains("NVML"), $"state={v.State} error={v.Error}");
            Assert(v.Gpus.Any(g => g.Alerts.Any(a => a.Kind == "lost" && a.Level == "crit")), "lost alert missing");
        }
        finally { File.Delete(failFlag); }
    }

    private static async Task<(int Code, string Err)> Docker(params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = await p.StandardError.ReadToEndAsync();
        await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, err);
    }
}
