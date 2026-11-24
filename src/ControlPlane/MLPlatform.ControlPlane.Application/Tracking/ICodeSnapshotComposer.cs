namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Builds the snapshot to run from a git commit fetched by the server (plus, from the SDK, the developer's
/// changed files); resolves branches and tags to commits.</summary>
public interface ICodeSnapshotComposer
{
    Task<string> ComposeAsync(Guid workspaceId, CodeSourceRow source, string gitRemote, string commit, string? overlaySha256,
        IReadOnlyList<string> deleted, CancellationToken ct);

    Task<string> ResolveCommitAsync(CodeSourceRow source, string gitRemote, string reference, CancellationToken ct);

    Task<string?> ReadFileAsync(Guid workspaceId, string snapshotSha256, string path, CancellationToken ct);
}
