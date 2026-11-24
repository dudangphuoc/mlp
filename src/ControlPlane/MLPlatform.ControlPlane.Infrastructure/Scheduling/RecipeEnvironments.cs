using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Recipes;
using Environment = MLPlatform.Contracts.V1.Environment;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>The recipe image ("environment") per machine (round 1 slice 2, D-44).</summary>
public sealed partial class RecipeEnvironments(AgentRegistry agents, AgentBuildStore builds, RecipeSourceStore sources, CatalogSource catalog, IServiceScopeFactory scopes, IConfiguration cfg,
    TimeProvider time, ILogger<RecipeEnvironments> log)
{
    private const int MaxFileBytes = 512 * 1024;
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(65);

    private readonly ConcurrentDictionary<(Guid HostId, string Tag), byte> _building = new();
    private readonly ConcurrentDictionary<InspectionKey, byte> _inspecting = new();
    private readonly ConcurrentDictionary<string, (long At, string? Tag)> _tags = new();

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex RecipeName();

    [GeneratedRegex(@"^recipes/([a-z0-9-]+):current$")]
    private static partial Regex RecipeImageName();

    [GeneratedRegex("No such image|Unable to find image|pull access denied|manifest unknown|repository does not exist", RegexOptions.IgnoreCase)]
    private static partial Regex ImageMissing();

    private string? RecipesDir => catalog.Directory;

    public static string Image(string recipe) => $"recipes/{recipe}:current";

    /// <summary>Container architecture of a machine (x64 | arm64): mlp-shim, and so the image tag, differs per
    /// architecture.</summary>
    public static string Arch(HostInfo? info) => (info?.Architecture ?? "").Contains("arm", StringComparison.OrdinalIgnoreCase) ? "arm64" : "x64";

    /// <summary>Signed link and hash of mlp-shim for the architecture; waits for agent-builder (up to 20
    /// minutes).</summary>
    public Task<(string Url, string Sha256)?> ShimAsync(string arch, CancellationToken ct) =>
        builds.ShimAsync(arch, cfg["Install:DownloadBase"] ?? cfg["PublicUrl"] ?? "", ct);

    /// <summary>Build command of a recipe image ( recipes/{name}:current ); its Tag is the environment tag; source
    /// archives still need <see cref="RecipeSourceStore.MaterializeAsync"/>.</summary>
    public ImageBuild? Build(string image, (string Url, string Sha256)? shim) => HostContainers.RecipeImage(image, RecipesDir, shim);

    /// <summary>The tag the recipe needs today on a machine of this architecture, without waiting: null while mlp-shim
    /// is not built yet or when the recipe describes no environment.</summary>
    public string? CurrentTag(string recipe, string arch)
    {
        if (builds.Version is not { } ver) return null;
        var st = builds.Status(ver, arch == "arm64" ? "shim-linux-arm64" : "shim-linux-x64");
        if (st["status"]?.ToString() != "ready" || st["sha256"]?.ToString() is not { } sha) return null;
        var key = $"{recipe}/{sha}";
        if (_tags.TryGetValue(key, out var hit) && time.GetElapsedTime(hit.At) < TimeSpan.FromSeconds(3)) return hit.Tag;
        var tag = HostContainers.RecipeImage(Image(recipe), RecipesDir, ("", sha))?.Tag;
        _tags[key] = (time.GetTimestamp(), tag);
        return tag;
    }

    /// <summary>The launcher built (or found) a recipe image on a machine, or failed to: remember it so the canvas
    /// knows where the environment is.</summary>
    public async Task RecordAsync(Guid workspaceId, string image, string tag, Guid hostId, bool ok, string error)
    {
        try
        {
            var m = RecipeImageName().Match(image);
            if (!m.Success) return;
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
                new RecordEnvironmentCommand(workspaceId, m.Groups[1].Value, tag, hostId, ok ? "ready" : "failed", ok ? "" : error), CancellationToken.None);
        }
        catch (Exception e) { log.LogWarning(e, "Recording environment {Tag} on {Host} failed", tag, hostId); }
    }

    // ── ListEnvironments ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Environment>> ListAsync(IMediator m, Guid ws, string recipe, CancellationToken ct)
    {
        await m.Query(new RequireCatalogPermissionQuery(ws, "run.read"), ct);
        await RecipeAsync(m, ws, recipe, ct);
        var rows = await m.Query(new ListEnvironmentRowsQuery(ws, recipe), ct);
        var res = new List<Environment>();
        foreach (var h in await m.Query(new ListWorkspaceHostsQuery(ws), ct))
        {
            var session = Session(ws, h.Id);
            var tag = CurrentTag(recipe, Arch(session?.Info ?? Info(h))) ?? "";
            res.Add(ToProto(h.Id, h.Name, tag, session is not null, rows.FirstOrDefault(r => r.HostId == h.Id && r.EnvTag == tag)));
        }
        return res;
    }

    // ── BuildEnvironment ────────────────────────────────────────────────────

    public async Task<Environment> BuildAsync(IMediator m, Guid ws, string recipe, Guid hostId, CancellationToken ct)
    {
        await m.Query(new RequireCatalogPermissionQuery(ws, "job.submit"), ct);
        await RecipeAsync(m, ws, recipe, ct);
        var host = (await m.Query(new ListWorkspaceHostsQuery(ws), ct)).FirstOrDefault(h => h.Id == hostId)
                   ?? throw new NotFoundException("FLEET_HOST_NOT_FOUND", "Machine not found");
        var session = Session(ws, hostId) ?? throw new UnavailableException("FLEET_HOST_OFFLINE", "The machine is offline");
        var arch = Arch(session.Info ?? Info(host));
        if (CurrentTag(recipe, arch) is not { } tag)
        {
            if (HostContainers.RecipeImage(Image(recipe), RecipesDir) is null)
                throw new PreconditionException("CATALOG_ENV_NO_DOCKERFILE", "The recipe has no environment (environment.base) on the control plane");
            _ = Task.Run(() => ShimAsync(arch, CancellationToken.None), CancellationToken.None);
            throw new UnavailableException("CATALOG_SHIM_NOT_READY", $"mlp-shim for linux-{arch} is not built yet; try again in a few minutes");
        }
        if (_building.TryAdd((hostId, tag), 0))
        {
            try { await m.Send(new RecordEnvironmentCommand(ws, recipe, tag, hostId, "building", ""), ct); }
            catch { _building.TryRemove((hostId, tag), out _); throw; }
            _ = Task.Run(() => RunBuildAsync(ws, recipe, tag, arch, session), CancellationToken.None);
        }
        return ToProto(hostId, host.Name, tag, true, await m.Query(new GetEnvironmentRowQuery(ws, tag, hostId), ct));
    }

    private async Task RunBuildAsync(Guid ws, string recipe, string tag, string arch, AgentSession session)
    {
        var (ok, error) = (false, "");
        try
        {
            var shim = await ShimAsync(arch, CancellationToken.None);
            if (shim is null) error = "mlp-shim is not available (agent-builder missing or its build failed)";
            else if (Build(Image(recipe), shim) is not { } rbuild || rbuild.Tag != tag) error = "The recipe changed while the build was starting; build again";
            else
            {
                var started = time.GetTimestamp();
                await sources.MaterializeAsync(rbuild, CancellationToken.None);
                var br = await session.SendCommandAsync(new AgentCommand { ImageBuild = rbuild }, BuildTimeout, CancellationToken.None);
                ok = br.Ok;
                if (!ok) error = $"{br.ErrorReason} {br.Message}".Trim();
                log.LogInformation("Environment {Tag} on {Host}: {Result} after {Min:0.0} min", tag, session.HostId,
                    ok ? br.Kept ? "already there" : "built" : "failed", time.GetElapsedTime(started).TotalMinutes);
            }
        }
        catch (Exception e)
        {
            error = e is TimeoutException ? $"No answer from the machine within {BuildTimeout.TotalMinutes:0} min" : "The build was interrupted: " + e.Message;
            log.LogWarning(e, "Environment build {Tag} on {Host} failed", tag, session.HostId);
        }
        finally
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
                    new RecordEnvironmentCommand(ws, recipe, tag, session.HostId, ok ? "ready" : "failed", ok ? "" : error.Length > 0 ? error : "Build failed"),
                    CancellationToken.None);
            }
            catch (Exception e) { log.LogError(e, "Recording environment {Tag} on {Host} failed", tag, session.HostId); }
            _building.TryRemove((session.HostId, tag), out _);
        }
    }

    // ── GetInspection ───────────────────────────────────────────────────────

    public async Task<Inspection> InspectAsync(IMediator m, Guid ws, string recipe, string variantName, string paramsJson, int numClasses,
        Guid? hostId, bool start, CancellationToken ct)
    {
        await m.Query(new RequireCatalogPermissionQuery(ws, "run.read"), ct);
        if (start) await m.Query(new RequireCatalogPermissionQuery(ws, "job.submit"), ct);
        var manifest = JsonNode.Parse((await RecipeAsync(m, ws, recipe, ct)).Manifest)!.AsObject();
        if (manifest["canvas"]?["inspect"]?.ToString() is not { Length: > 0 })
            return new Inspection { State = "none", Error = "The recipe does not define an inspection (canvas.inspect)" };
        var variant = InspectionRules.Variant(manifest, variantName)
                      ?? throw new NotFoundException("CATALOG_VARIANT_NOT_FOUND", $"The recipe has no variant {variantName}");
        var set = InspectionRules.ParseParams(paramsJson);
        var sha = InspectionRules.ParamsHash(manifest, set);
        numClasses = Math.Max(0, numClasses);

        var ready = await ReadyEnvironmentsAsync(m, ws, recipe, hostId, ct);
        if (ready.Count == 0) return new Inspection { State = "no_environment" };
        ready = [.. ready.OrderBy(r => r.Session is null)];

        foreach (var tag in ready.Select(r => r.Tag).Distinct())
        {
            if (await m.Query(new GetInspectionRowQuery(new InspectionKey(ws, tag, variantName, sha, numClasses)), ct) is not { } row) continue;
            var retry = start && row.State == "failed" && time.GetUtcNow() - row.CreatedAt > InspectionRules.RunLimit;
            if (!retry) return ToProto(row, manifest);
        }
        if (!start) return new Inspection { State = "none", EnvTag = ready[0].Tag };
        if (ready[0].Session is not { } target) throw new UnavailableException("FLEET_HOST_OFFLINE", "No machine with this environment is online");
        return await StartInspectionAsync(m, ws, recipe, variantName, numClasses, manifest, variant, set, sha, ready[0].Tag, target, ct);
    }

    private async Task<List<(string Tag, AgentSession? Session)>> ReadyEnvironmentsAsync(IMediator m, Guid ws, string recipe, Guid? hostId, CancellationToken ct)
    {
        var hosts = await m.Query(new ListWorkspaceHostsQuery(ws), ct);
        if (hostId is { } only)
        {
            hosts = [.. hosts.Where(h => h.Id == only)];
            if (hosts.Count == 0) throw new NotFoundException("FLEET_HOST_NOT_FOUND", "Machine not found");
        }
        var envs = await m.Query(new ListEnvironmentRowsQuery(ws, recipe), ct);
        var ready = new List<(string Tag, AgentSession? Session)>();
        foreach (var h in hosts)
        {
            var session = Session(ws, h.Id);
            if (CurrentTag(recipe, Arch(session?.Info ?? Info(h))) is { } tag && envs.Any(e => e.HostId == h.Id && e.EnvTag == tag && e.State == "ready"))
                ready.Add((tag, session));
        }
        return ready;
    }

    private async Task<Inspection> StartInspectionAsync(IMediator m, Guid ws, string recipe, string variantName, int numClasses, JsonObject manifest,
        JsonObject variant, JsonObject set, string sha, string envTag, AgentSession target, CancellationToken ct)
    {
        string command;
        try { command = InspectionRules.Command(manifest, variant, set, numClasses); }
        catch (TemplateException e) { throw new PreconditionException("CATALOG_INSPECT_TEMPLATE", $"canvas.inspect cannot be built ({e.Reason}): {e.Message}"); }
        var key = new InspectionKey(ws, envTag, variantName, sha, numClasses);
        if (!_inspecting.TryAdd(key, 0))
            return await m.Query(new GetInspectionRowQuery(key), ct) is { } now ? ToProto(now, manifest) : new Inspection { State = "running", EnvTag = key.EnvTag };
        InspectionRow started;
        try { started = await m.Send(new StartInspectionCommand(key, recipe), ct); }
        catch { _inspecting.TryRemove(key, out _); throw; }
        var workdir = manifest["canvas"]?["inspectWorkdir"]?.ToString() ?? "";
        _ = Task.Run(() => RunInspectionAsync(key, recipe, command, workdir, target), CancellationToken.None);
        return ToProto(started, manifest);
    }

    private async Task RunInspectionAsync(InspectionKey key, string recipe, string command, string workdir, AgentSession session)
    {
        try
        {
            InspectionOutput output;
            var imageGone = false;
            try
            {
                var spec = new ContainerSpec { Image = key.EnvTag, Cpus = 2, Init = true, Workdir = workdir };
                spec.Labels["mlp.role"] = "inspect";
                spec.Entrypoint.Add("sh");
                spec.Command.AddRange(["-c", command]);
                var limit = (int)InspectionRules.RunLimit.TotalSeconds;
                var r = await session.SendCommandAsync(new AgentCommand { ContainerRunOnce = new ContainerRunOnce { Spec = spec, TimeoutSeconds = limit } },
                    InspectionRules.RunLimit + TimeSpan.FromSeconds(30), CancellationToken.None);
                imageGone = !r.Ok && ImageMissing().IsMatch(r.Message);
                output = InspectionRules.Parse(r.Output, r.Ok, r.Message.Length > 0 ? r.Message : r.ErrorReason, ErrorHeadline.From);
            }
            catch (Exception e)
            {
                log.LogWarning(e, "Inspection of {Recipe} on {Host} failed", recipe, session.HostId);
                output = new InspectionOutput(false, e is TimeoutException ? "The inspection did not finish within 5 minutes" : "The machine did not answer: " + e.Message, "", "", "[]");
            }
            await using var scope = scopes.CreateAsyncScope();
            var m = scope.ServiceProvider.GetRequiredService<IMediator>();
            if (imageGone)
            {
                await m.Send(new RecordEnvironmentCommand(key.WorkspaceId, recipe, key.EnvTag, session.HostId, "failed",
                    "The image is no longer on the machine; build the environment again"), CancellationToken.None);
                await m.Send(new DeleteInspectionCommand(key), CancellationToken.None);
            }
            else await m.Send(new FinishInspectionCommand(key, output), CancellationToken.None);
        }
        catch (Exception e) { log.LogError(e, "Recording the inspection of {Recipe} failed", recipe); }
        finally { _inspecting.TryRemove(key, out _); }
    }

    // ── Recipe files ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RecipeFile>> ListFilesAsync(IMediator m, Guid ws, string recipe, CancellationToken ct)
    {
        await m.Query(new RequireCatalogPermissionQuery(ws, "run.read"), ct);
        var res = new List<RecipeFile>();
        if ((await RecipeAsync(m, ws, recipe, ct)).WorkspaceId is not null || Root() is not { } root) return res;
        foreach (var dir in new[] { recipe, "_shared" })
        {
            var abs = Path.Combine(root, dir);
            if (!Directory.Exists(abs)) continue;
            foreach (var f in Directory.EnumerateFiles(abs, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                var info = new FileInfo(f);
                if (rel.Contains("/__pycache__/", StringComparison.Ordinal) || info.LinkTarget is not null || await IsBinaryAsync(f, ct)) continue;
                res.Add(new RecipeFile { Path = rel, SizeBytes = info.Length });
            }
        }
        return res;
    }

    public async Task<RecipeFileContent> ReadFileAsync(IMediator m, Guid ws, string recipe, string path, CancellationToken ct)
    {
        await m.Query(new RequireCatalogPermissionQuery(ws, "run.read"), ct);
        var row = await RecipeAsync(m, ws, recipe, ct);
        NotFoundException Missing() => new("CATALOG_FILE_NOT_FOUND", "File not found");
        var parts = path.Split('/');
        if (row.WorkspaceId is not null || Root() is not { } root || path.Length is 0 or > 512 || path.Contains('\\') || path.Contains('\0')
            || parts.Length < 2 || (parts[0] != recipe && parts[0] != "_shared") || parts.Any(p => p.Length == 0 || p is "." or ".."))
            throw Missing();
        var full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full) || new FileInfo(full).LinkTarget is not null)
            throw Missing();
        if (await IsBinaryAsync(full, ct)) throw new PreconditionException("CATALOG_FILE_BINARY", "The file is not text");
        await using var fs = File.OpenRead(full);
        var buf = new byte[(int)Math.Min(fs.Length, MaxFileBytes)];
        await fs.ReadExactlyAsync(buf, ct);
        return new RecipeFileContent { Path = path, Text = System.Text.Encoding.UTF8.GetString(buf), Truncated = fs.Length > MaxFileBytes };
    }

    private string? Root() => string.IsNullOrEmpty(RecipesDir) || !Directory.Exists(RecipesDir) ? null : Path.GetFullPath(RecipesDir).TrimEnd(Path.DirectorySeparatorChar);

    private static async Task<bool> IsBinaryAsync(string file, CancellationToken ct)
    {
        var buf = new byte[4096];
        await using var fs = File.OpenRead(file);
        var n = await fs.ReadAsync(buf, ct);
        return buf.AsSpan(0, n).Contains((byte)0);
    }

    // ── Shared ──────────────────────────────────────────────────────────────

    /// <summary>The recipe as the workspace sees it (builtin or its own); the name is checked first because it becomes
    /// a path and an image name.</summary>
    private static async Task<RecipeRow> RecipeAsync(IMediator m, Guid ws, string recipe, CancellationToken ct)
    {
        if (!RecipeName().IsMatch(recipe)) throw new ValidationException(new Dictionary<string, string> { ["recipe"] = "Invalid recipe name" });
        return await m.Query(new GetRecipeQuery(ws, recipe), ct);
    }

    private AgentSession? Session(Guid ws, Guid hostId) => agents.TryGet(hostId, out var s) && s.WorkspaceId == ws ? s : null;

    private static HostInfo? Info(HostRow h) => h.HostInfo is { Length: > 0 } b ? HostInfo.Parser.ParseFrom(b) : null;

    private static Environment ToProto(Guid hostId, string hostName, string tag, bool online, EnvironmentRow? row)
    {
        var p = new Environment { HostId = hostId.ToString(), HostName = hostName, EnvTag = tag, Online = online, State = row?.State ?? "unknown", Error = row?.Error ?? "" };
        if (row is not null) p.StartedAt = Timestamp.FromDateTimeOffset(row.StartedAt);
        if (row?.BuiltAt is { } built) p.BuiltAt = Timestamp.FromDateTimeOffset(built);
        return p;
    }

    private static Inspection ToProto(InspectionRow row, JsonObject manifest)
    {
        var p = new Inspection
        {
            State = row.State, Error = row.Error, OverlayYaml = row.OverlayYaml, ConfigJson = row.ConfigJson, EnvTag = row.EnvTag, SourcesJson = row.Sources,
            CreatedAt = Timestamp.FromDateTimeOffset(row.CreatedAt),
        };
        var labels = (manifest["canvas"]?["blocks"] as JsonArray)?.OfType<JsonObject>() ?? [];
        foreach (var b in (JsonNode.Parse(row.Blocks) as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var key = b["key"]?.ToString() ?? "";
            p.Blocks.Add(new ModelBlock
            {
                Key = key, Label = labels.FirstOrDefault(l => l["key"]?.ToString() == key)?["label"]?.ToString() ?? key,
                Params = InspectionRules.Long(b["params"]), Trainable = InspectionRules.Long(b["trainable"]),
            });
        }
        return p;
    }
}
