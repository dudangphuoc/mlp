using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Reference model version: store the source address of each file instead of copying (import worker,
/// internal).</summary>
public sealed record SetModelVersionReferenceCommand(Guid VersionId, IReadOnlyList<(string Path, long Size, string Url)> Files) : ICommand<bool>, IInternalCommand;

/// <summary>Handles <see cref="SetModelVersionReferenceCommand"/>.</summary>
public sealed class SetModelVersionReferenceCommandHandler(IWriteSession db) : ICommandHandler<SetModelVersionReferenceCommand, bool>
{
    public async ValueTask<bool> Handle(SetModelVersionReferenceCommand c, CancellationToken ct)
    {
        foreach (var f in c.Files) await db.InsertVersionFileRefAsync(c.VersionId, MLPlatform.ControlPlane.Application.Data.Buckets.SafePath(f.Path), f.Size, f.Url, ct);
        await db.SetVersionReferenceAsync(c.VersionId, ct);
        await db.SetModelVersionSizeAsync(c.VersionId, c.Files.Count, c.Files.Sum(f => f.Size), ct);
        return true;
    }
}
