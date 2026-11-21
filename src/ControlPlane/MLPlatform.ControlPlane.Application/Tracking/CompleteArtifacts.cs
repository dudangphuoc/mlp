using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

public sealed record CompleteArtifactsCommand(string WorkloadToken, Guid RunId, IReadOnlyList<(string Path, long Size)> Files) : ICommand<int>;

public sealed class CompleteArtifactsCommandHandler(IWriteSession db, TokenService tokens) : ICommandHandler<CompleteArtifactsCommand, int>
{
    public async ValueTask<int> Handle(CompleteArtifactsCommand c, CancellationToken ct)
    {
        var run = await WorkloadAuth.RequireRunAsync(db, tokens, c.WorkloadToken, c.RunId, ct);
        foreach (var (path, size) in c.Files) await db.UpsertRunArtifactAsync(run.Id, Buckets.SafePath(path), size, ct);
        return c.Files.Count;
    }
}
