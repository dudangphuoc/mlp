using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Infrastructure;

/// <summary>
/// Agent builds per platform (D-37/D-38). The control plane holds the agent source; the <c>agent-builder</c> service builds on demand and caches:
/// <c>{AgentDir}/source-version</c>, <c>queue/{ver}_{rid}.json</c>, <c>cache/{ver}/{rid}/{mlp-agent, .sha256, build.json, build.log}</c>.
/// Download links are always HMAC-signed + time-limited (blogic-tunnels pattern), used by both the install command and OTA.
/// </summary>
public sealed class AgentBuildStore(IConfiguration cfg, TimeProvider time, ILogger<AgentBuildStore> log)
{
    public static readonly string[] Supported = ["linux-x64", "linux-arm64", "linux-musl-x64", "linux-musl-arm64", "osx-arm64", "osx-x64", "win-x64"];

    private string Dir => cfg["Install:AgentDir"] ?? Path.Combine(AppContext.BaseDirectory, "agents");
    private byte[] Key => HMACSHA256.HashData(Encoding.UTF8.GetBytes(cfg["Secrets:TokenPepper"] ?? "dev"), "agent-release-links"u8);

    /// <summary>Current agent version (source hash written by agent-builder); null when there is no builder yet.</summary>
    public string? Version => File.Exists(Path.Combine(Dir, "source-version")) ? File.ReadAllText(Path.Combine(Dir, "source-version")).Trim() : null;

    /// <summary>Linux mlp-shim builds (baked into recipe images for training), cached by the same builder as "shim-{rid}".</summary>
    public static readonly string[] ShimRids = ["shim-linux-x64", "shim-linux-arm64"];

    public static string Exe(string rid) => rid.StartsWith("shim-", StringComparison.Ordinal) ? "mlp-shim"
        : rid.StartsWith("win", StringComparison.Ordinal) ? "mlp-agent.exe" : "mlp-agent";

    /// <summary>Absolute signed download URL of mlp-shim for a container architecture (x64 | arm64), once built (waits up to 20 minutes).</summary>
    public async Task<(string Url, string Sha256)?> ShimAsync(string arch, string downloadBase, CancellationToken ct)
    {
        if (Version is not { } ver) return null;
        var rid = arch == "arm64" ? "shim-linux-arm64" : "shim-linux-x64";
        var st = await EnsureAsync(ver, rid, TimeSpan.FromMinutes(20), ct);
        if (st["status"]?.ToString() != "ready") return null;
        return (downloadBase.TrimEnd('/') + SignedPath(ver, rid, TimeSpan.FromHours(2)), st["sha256"]!.ToString());
    }

    /// <summary>RID from the doctor result (os/arch/libc); null when not supported.</summary>
    public static string? Rid(JsonObject d)
    {
        var os = (d["os"]?.ToString() ?? "").ToLowerInvariant();
        var arch = (d["arch"]?.ToString() ?? "").ToLowerInvariant() switch { "x86_64" or "amd64" or "x64" => "x64", "aarch64" or "arm64" => "arm64", _ => null };
        if (arch is null) return null;
        return os switch
        {
            "linux" => d["libc"]?.ToString() == "musl" ? $"linux-musl-{arch}" : $"linux-{arch}",
            "darwin" => $"osx-{arch}",
            "windows" => arch == "x64" ? "win-x64" : null,
            _ => null,
        };
    }

    private string Sign(string ver, string rid, string file, long expires) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes($"{ver}/{rid}/{file}/{expires}")))[..32];

    /// <summary>Signed download path, expires after <paramref name="ttl"/>.</summary>
    public string SignedPath(string ver, string rid, TimeSpan ttl)
    {
        var e = time.GetUtcNow().Add(ttl).ToUnixTimeSeconds();
        return $"/install/agent/{ver}/{rid}/{Exe(rid)}?e={e}&s={Sign(ver, rid, Exe(rid), e)}";
    }

    public bool Verify(string ver, string rid, string file, long expires, string sig) =>
        expires >= time.GetUtcNow().ToUnixTimeSeconds()
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(ver, rid, file, expires)), Encoding.ASCII.GetBytes(sig ?? ""));

    public string CachePath(string ver, string rid, string file) => Path.Combine(Dir, "cache", ver, rid, file);

    /// <summary>Build status: ready (sha256 + 1-hour signed link) | building | failed (log tail).</summary>
    public JsonObject Status(string ver, string rid)
    {
        var dir = Path.Combine(Dir, "cache", ver, rid);
        var o = new JsonObject { ["rid"] = rid, ["version"] = ver };
        var sha = Path.Combine(dir, Exe(rid) + ".sha256");
        if (File.Exists(sha))
        {
            o["status"] = "ready";
            o["sha256"] = File.ReadAllText(sha).Split(' ')[0].Trim();
            o["url"] = SignedPath(ver, rid, TimeSpan.FromHours(1));
            if (File.Exists(Path.Combine(dir, "build.json"))) o["build"] = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "build.json")));
        }
        else if (File.Exists(Path.Combine(dir, "failed")))
        {
            o["status"] = "failed";
            var logf = Path.Combine(dir, "build.log");
            o["log"] = File.Exists(logf) ? string.Join('\n', File.ReadLines(logf).TakeLast(30)) : "";
        }
        else o["status"] = "building";
        return o;
    }

    /// <summary>Queue a build request if none exists (retry: clear the failure marker to rebuild).</summary>
    public JsonObject Request(string ver, string rid, JsonObject? doctor, bool retry = false)
    {
        var st = Status(ver, rid);
        if (st["status"]!.ToString() == "ready") return st;
        var dir = Path.Combine(Dir, "cache", ver, rid);
        if (st["status"]!.ToString() == "failed" && retry) File.Delete(Path.Combine(dir, "failed"));
        Directory.CreateDirectory(Path.Combine(Dir, "queue"));
        var req = Path.Combine(Dir, "queue", $"{ver}_{rid}.json");
        if (!File.Exists(req) && !File.Exists(Path.Combine(dir, "failed")))
        {
            File.WriteAllText(req, new JsonObject { ["rid"] = rid, ["version"] = ver, ["doctor"] = doctor?.DeepClone() }.ToJsonString());
            log.LogInformation("Queued agent build {Rid} (source {Ver})", rid, ver);
        }
        return Status(ver, rid);
    }

    /// <summary>Wait for the build to be ready (up to <paramref name="max"/>).</summary>
    public async Task<JsonObject> EnsureAsync(string ver, string rid, TimeSpan max, CancellationToken ct)
    {
        var until = time.GetUtcNow() + max;
        var st = Request(ver, rid, null, retry: true);
        while (st["status"]!.ToString() == "building" && time.GetUtcNow() < until)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            st = Status(ver, rid);
        }
        return st;
    }
}

/// <summary>Plug-and-play install endpoint (script sends doctor → per-host build → signed download).</summary>
public static class AgentBuildEndpoints
{
    public static void MapAgentBuilds(this WebApplication app)
    {
        var store = app.Services.GetRequiredService<AgentBuildStore>();

        async Task<bool> Authorized(HttpContext ctx)
        {
            var h = ctx.Request.Headers.Authorization.ToString();
            var token = h.StartsWith("Bearer ", StringComparison.Ordinal) ? h[7..] : "";
            return await ctx.RequestServices.GetRequiredService<IMediator>().Query(new EnrollTokenValidQuery(token), ctx.RequestAborted);
        }

        app.MapPost("/install/agent/build", async (HttpContext ctx) =>
        {
            if (!await Authorized(ctx)) return Results.Text("The install command has expired or was already used", statusCode: 401);
            if (await JsonNode.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted) is not JsonObject doctor)
                return Results.Text("Missing doctor result", statusCode: 400);
            if (AgentBuildStore.Rid(doctor) is not { } rid) return Results.Text($"Platform not supported yet: {doctor["os"]}/{doctor["arch"]}", statusCode: 422);
            if (store.Version is not { } ver) return Results.Text("The control plane has no agent-builder yet (missing source-version)", statusCode: 503);
            return Results.Text(store.Request(ver, rid, doctor, doctor["retry"]?.GetValue<bool>() == true).ToJsonString(), "application/json");
        });
        app.MapGet("/install/agent/build/{rid}", (string rid) =>
            !AgentBuildStore.Supported.Contains(rid) ? Results.NotFound()
            : store.Version is { } ver ? Results.Text(store.Status(ver, rid).ToJsonString(), "application/json") : Results.StatusCode(503));
        // Binary download: requires a valid, unexpired HMAC signature (link issued by /install/agent/build or OTA).
        app.MapGet("/install/agent/{ver}/{rid}/{file}", (string ver, string rid, string file, long? e, string? s) =>
        {
            if (!(AgentBuildStore.Supported.Contains(rid) || AgentBuildStore.ShimRids.Contains(rid)) || file != AgentBuildStore.Exe(rid) || ver.Any(ch => !char.IsAsciiLetterOrDigit(ch))) return Results.NotFound();
            if (e is null || !store.Verify(ver, rid, file, e.Value, s ?? "")) return Results.StatusCode(403);
            var path = store.CachePath(ver, rid, file);
            return File.Exists(path) ? Results.File(path, "application/octet-stream", file) : Results.NotFound();
        });
    }
}
