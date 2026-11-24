using System.Security.Cryptography;
using System.Text;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Shared checks for runs opened by the SDK from the developer's own code.</summary>
public static class CodeRunRules
{
    public const string DefaultTask = "custom";
    public const string EnvImagePrefix = "mlp/code-env:";
    public const long MaxSnapshotBytes = 200L << 20;
    public const long MaxDataBytes = 5L << 30;
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(90);

    public static async Task<CodeRunRow> RequireAsync(IReadSession db, WorkspaceAuthorizer auth, Guid workspaceId, Guid runId, CancellationToken ct)
    {
        await auth.RequireAsync(workspaceId, "job.submit", ct);
        return await db.GetCodeRunAsync(runId, workspaceId, ct) ?? throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
    }

    public static async Task<MembershipRow> PickWorkspaceAsync(IReadSession db, ICurrentUser user, string? wanted, CancellationToken ct)
    {
        var mine = (await db.ListMembershipsAsync(user.RequireUser(), ct))
            .Where(m => user.KeyWorkspaceId is not { } only || m.Id == only).ToList();
        if (!string.IsNullOrWhiteSpace(wanted))
            return mine.FirstOrDefault(m => m.Slug == wanted.Trim() || m.Id.ToString() == wanted.Trim())
                   ?? throw new NotFoundException("IAM_WORKSPACE_NOT_FOUND", $"Workspace '{wanted}' not found for this key");
        return mine.Count == 1 ? mine[0]
            : throw new PreconditionException("TRACK_WORKSPACE_REQUIRED",
                $"This key reaches several workspaces: set MLP_WORKSPACE to one of {string.Join(", ", mine.Select(m => m.Slug))}");
    }

    public static string SnapshotKey(Guid workspaceId, string sha256) => $"{workspaceId}/code/{sha256}.tar.gz";

    public static string DataKey(Guid workspaceId, string sha256) => $"{workspaceId}/code/data/{sha256}.tar.gz";

    public static bool IsSha256(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);

    public static string EnvImage(string platformFingerprint, string baseImage, string requirements) =>
        EnvImagePrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{platformFingerprint}\n{baseImage}\n{requirements}")))[..16];

    public static string StateForExit(int exitCode) => exitCode switch
    {
        0 => "succeeded",
        130 => "cancelled",
        _ => "failed",
    };
}
