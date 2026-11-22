using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MLPlatform.Agent;

// mlp-agent [--state ] [--cp host:port] [--token mlpe_…] [--data ] [--models ] [--pin ] [--service
// systemd|launchd|schtasks] [--insecure] [--instance dev] [--gpu-priority normal|yield] [--fs-root ]...
string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
var instance = Arg("--instance") ?? "";
var statePath = Arg("--state") ?? Path.Combine(AgentOptions.DefaultStateDir(instance), "agent.json");
var state = new AgentState(statePath);
var o = state.Options;
if (Arg("--cp") is { } cp) o.ControlPlane = cp;
if (Arg("--token") is { } t)
{
    var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(t)));
    if (hash != o.UsedEnrollTokenHash) { o.EnrollToken = t; o.UsedEnrollTokenHash = hash; }
}
if (Arg("--data") is { } d) o.DataPath = d;
if (Arg("--models") is { } m) o.ModelsPath = m;
if (Arg("--pin") is { } pin) { o.CertPin = pin; o.UseTls = true; }
if (args.Contains("--insecure")) o.UseTls = false;
if (Arg("--service") is { } svc) o.ServiceManager = svc;
if (Arg("--instance") is { } inst) { o.Instance = inst; o.GpuPriority = inst.Length > 0 ? "yield" : "normal"; }
if (Arg("--gpu-priority") is { } prio and ("normal" or "yield")) o.GpuPriority = prio;
var fsRoots = args.Select((a, i) => (a, i)).Where(x => x.a == "--fs-root" && x.i + 1 < args.Length).Select(x => args[x.i + 1]).ToList();
if (fsRoots.Count > 0) o.FsRoots = fsRoots;
if (string.IsNullOrEmpty(o.ControlPlane) || (o.EnrollToken is null && o.Credential is null))
{
    Console.Error.WriteLine("Missing --cp and --token (first run) or saved credentials");
    return 2;
}
state.Save();

if (OperatingSystem.IsWindows() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_CONFIG")))
{
    var dockerConfig = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(statePath))!, "docker");
    Directory.CreateDirectory(dockerConfig);
    File.WriteAllText(Path.Combine(dockerConfig, "docker-credential-mlpnone.cmd"),
        "@echo off\r\nif \"%1\"==\"get\" (echo credentials not found in native keychain& exit /b 1)\r\nif \"%1\"==\"list\" (echo {}& exit /b 0)\r\nexit /b 0\r\n");
    File.WriteAllText(Path.Combine(dockerConfig, "config.json"), "{\"auths\":{},\"credsStore\":\"mlpnone\"}");
    Environment.SetEnvironmentVariable("DOCKER_CONFIG", dockerConfig);
    Environment.SetEnvironmentVariable("PATH", dockerConfig + ";" + Environment.GetEnvironmentVariable("PATH"));
}

HostOwnership.ClaimsUnlabeled = o.Instance.Length == 0;
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(state);
if (o.ServiceManager is "schtasks" or "launchd" || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
    builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(state.Dir, "agent.log")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LogShipper>();
builder.Services.AddSingleton<Containers>();
builder.Services.AddSingleton<MLPlatform.Agent.Ops.RemoteOps>();
builder.Services.AddSingleton<CommandHandler>();
builder.Services.AddHostedService<AgentService>();
builder.Services.Configure<HostOptions>(h => h.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
await builder.Build().RunAsync();
return 0;
