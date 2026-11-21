using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MLPlatform.Agent;

// mlp-agent [--state <file>] [--cp host:port] [--token mlpe_…] [--data <dir>] [--models <dir>] [--pin <sha256>] [--service systemd|launchd|schtasks] [--insecure]
string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
var statePath = Arg("--state") ?? Path.Combine(AgentOptions.DefaultStateDir(), "agent.json");
var state = new AgentState(statePath);
var o = state.Options;
if (Arg("--cp") is { } cp) o.ControlPlane = cp;
if (Arg("--token") is { } t)
{
    // Install command rerun with a NEW token → enroll again (host_id kept thanks to the old credentials, 20/04 §2 step 6);
    // service restarted with an already-used token → ignore it and use the saved credentials.
    var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(t)));
    if (hash != o.UsedEnrollTokenHash) { o.EnrollToken = t; o.UsedEnrollTokenHash = hash; }
}
if (Arg("--data") is { } d) o.DataPath = d;
if (Arg("--models") is { } m) o.ModelsPath = m;
if (Arg("--pin") is { } pin) { o.CertPin = pin; o.UseTls = true; }
if (args.Contains("--insecure")) o.UseTls = false;
if (Arg("--service") is { } svc) o.ServiceManager = svc;
if (string.IsNullOrEmpty(o.ControlPlane) || (o.EnrollToken is null && o.Credential is null))
{
    Console.Error.WriteLine("Missing --cp and --token (first run) or saved credentials");
    return 2;
}
state.Save();

// Windows (Docker Desktop): the docker CLI uses the wincred/desktop credential helper, which needs an interactive logon session; a Scheduled Task (S4U)
// has none → image pulls fail. Use a separate DOCKER_CONFIG + an empty helper "mlpnone" (always returns no credentials).
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

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(state);
// Services have no console (Windows Scheduled Task, launchd; systemd keeps a journal): write agent.log in the agent folder.
// Also on every Windows/macOS start, whatever launched the agent (OTA reinstalls start mlp-agent.exe directly) — the control plane reads this file.
if (o.ServiceManager is "schtasks" or "launchd" || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
    builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(state.Dir, "agent.log")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LogShipper>();
builder.Services.AddSingleton<Containers>();
builder.Services.AddSingleton<CommandHandler>();
builder.Services.AddHostedService<AgentService>();
// One failing background service must not bring down the whole agent (the host would lose its connection until the task restarts itself).
builder.Services.Configure<HostOptions>(h => h.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
await builder.Build().RunAsync();
return 0;
