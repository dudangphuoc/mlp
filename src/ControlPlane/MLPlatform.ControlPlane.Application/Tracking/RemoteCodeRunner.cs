using System.Text.Json;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Turns uploaded code into a job on a cluster machine: environment image keyed by base image and
/// requirements, code fetched from the snapshot when the container starts.</summary>
public sealed partial class RemoteCodeRunner(IWriteSession db, IClock clock, S3Client s3, IMediator mediator, ICodeSnapshotComposer composer, ICodeEnvironment environment)
{
    public const string DefaultImage = "python:3.12-slim";
    private const int MaxRequirementsBytes = 64 * 1024;

    public async Task StartAsync(NewCodeRun run, RemoteCodeRun r, CancellationToken ct)
    {
        var (hostId, hostName) = await MachineAsync(run.WorkspaceId, r.Machine.Trim(), ct);
        await RequireSnapshotAsync(run.WorkspaceId, r.SnapshotSha256, ct);
        if (r.BaseCommit.Length > 0) r = r with { SnapshotSha256 = await ComposeAsync(run.WorkspaceId, r, ct) };
        var image = string.IsNullOrWhiteSpace(r.Image) ? DefaultImage : r.Image.Trim();
        if (!ImageName().IsMatch(image)) throw new PreconditionException("TRACK_IMAGE_INVALID", $"MLP_IMAGE '{image}' is not an image name");
        if (r.Requirements.Length > MaxRequirementsBytes) throw new PreconditionException("TRACK_REQUIREMENTS_TOO_LARGE", "requirements.txt is larger than 64 KB");
        var spec = new JobSpecDto
        {
            Image = CodeRunRules.EnvImage(environment.Fingerprint, image, r.Requirements), Command = ["python", "-m", "mlplatform.launch"], Cpus = 1, Gpus = Math.Max(0, r.Gpus),
            Hosts = hostId is { } h ? [h] : [], Env = JobEnv(run.WorkspaceId, image, r),
        };
        if (r.Data is { Count: > 0 } data) spec.Env["MLP_DATA_MAP"] = await DataMapAsync(run.WorkspaceId, data, ct);
        var jobId = await mediator.Send(new SubmitJobCommand(run.WorkspaceId, "train", $"{run.Project.Slug} #{run.Number}", spec), ct);
        await db.InsertRemoteCodeRunAsync(run.Id, run.Project.Id, run.WorkspaceId, run.Name, jobId, run.UserId, run.Project.Task, run.ParamsJson,
            clock.UtcNow, run.Number, spec.Gpus, hostName, run.CodeJson, ct);
    }

    private async Task<(Guid? Id, string Name)> MachineAsync(Guid workspaceId, string machine, CancellationToken ct)
    {
        if (machine.Equals("auto", StringComparison.OrdinalIgnoreCase)) return (null, "");
        var hosts = await mediator.Query(new ListWorkspaceHostsQuery(workspaceId), ct);
        var host = hosts.FirstOrDefault(h => h.Name.Equals(machine, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotFoundException("TRACK_MACHINE_NOT_FOUND",
                $"Machine '{machine}' is not in this workspace: use auto or one of {string.Join(", ", hosts.Select(h => h.Name))}");
        if (host.State is not ("online" or "busy")) throw new PreconditionException("TRACK_MACHINE_OFFLINE", $"Machine '{host.Name}' is {host.State}");
        return (host.Id, host.Name);
    }

    private async Task<string> ComposeAsync(Guid workspaceId, RemoteCodeRun r, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(r.BaseCommit, "^[0-9a-f]{40}$"))
            throw new PreconditionException("TRACK_BASE_COMMIT", "Base commit must be a full 40-character SHA");
        var source = CodeSourceUrls.Match(await db.ListCodeSourcesAsync(workspaceId, ct), r.GitRemote)
            ?? throw new PreconditionException("TRACK_SOURCE_NOT_FOUND", $"No code source covers {CodeSourceUrls.Normalize(r.GitRemote)}");
        return await composer.ComposeAsync(workspaceId, source, r.GitRemote, r.BaseCommit, r.SnapshotSha256, r.Deleted ?? [], ct);
    }

    private async Task RequireSnapshotAsync(Guid workspaceId, string sha256, CancellationToken ct)
    {
        if (!CodeRunRules.IsSha256(sha256) || await s3.HeadAsync(Buckets.Artifacts, CodeRunRules.SnapshotKey(workspaceId, sha256), ct) is null)
            throw new PreconditionException("TRACK_SNAPSHOT_MISSING", "The code snapshot was not uploaded");
    }

    private async Task<string> DataMapAsync(Guid workspaceId, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        var map = new System.Text.Json.Nodes.JsonObject();
        foreach (var (path, value) in data)
        {
            var (sha, name) = value.IndexOf(':') is var colon and > 0 ? (value[..colon], value[(colon + 1)..]) : (value, "");
            var key = CodeRunRules.DataKey(workspaceId, sha);
            if (!CodeRunRules.IsSha256(sha) || name.Length == 0 || name.Contains('/') || await s3.HeadAsync(Buckets.Artifacts, key, ct) is null)
                throw new PreconditionException("TRACK_DATA_MISSING", $"Local data {path} was not uploaded");
            map[path] = new System.Text.Json.Nodes.JsonObject
                { ["sha256"] = sha, ["name"] = name, ["url"] = s3.Presign(HttpMethod.Get, Buckets.Artifacts, key, TimeSpan.FromDays(7), Audience.Workload) };
        }
        return map.ToJsonString();
    }

    private Dictionary<string, string> JobEnv(Guid workspaceId, string image, RemoteCodeRun r)
    {
        var env = r.Env.Where(kv => EnvName().IsMatch(kv.Key) && !kv.Key.StartsWith("MLP_", StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);
        env["MLP_CODE_URL"] = s3.Presign(HttpMethod.Get, Buckets.Artifacts, CodeRunRules.SnapshotKey(workspaceId, r.SnapshotSha256), TimeSpan.FromDays(7), Audience.Workload);
        env["MLP_CODE_WORKDIR"] = r.Workdir.Replace('\\', '/').Trim('/');
        env["MLP_CODE_COMMAND"] = JsonSerializer.Serialize(r.Command.ToList(), SchedJson.Default.ListString);
        env["MLP_CODE_ENV_BASE"] = image;
        env["MLP_CODE_ENV_REQUIREMENTS"] = r.Requirements;
        if (r.Debug is { } d)
        {
            env["MLP_DEBUG_TOKEN"] = d.Token;
            env["MLP_DEBUG_PPID"] = d.ParentPid.ToString(System.Globalization.CultureInfo.InvariantCulture);
            env["PATHS_FROM_ECLIPSE_TO_PYTHON"] = JsonSerializer.Serialize(new List<List<string>> { new() { d.Root, "/work" } }, SchedJson.Default.ListListString);
        }
        return env;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/:@-]{0,254}$")]
    private static partial Regex ImageName();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex EnvName();
}
