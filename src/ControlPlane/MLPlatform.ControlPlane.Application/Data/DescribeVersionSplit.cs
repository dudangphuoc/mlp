using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Reads a version's split (looking at its files once and remembering the answer) for the training canvas.</summary>
public sealed record DescribeVersionSplitCommand(Guid WorkspaceId, Guid VersionId) : ICommand<VersionSplitInfo>;

/// <summary>Detects train/val folders from the file list the first time; versions made by <see cref="SplitVersionCommand"/> already carry their counts.</summary>
public sealed class DescribeVersionSplitCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<DescribeVersionSplitCommand, VersionSplitInfo>
{
    public async ValueTask<VersionSplitInfo> Handle(DescribeVersionSplitCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.read", ct);
        var v = await db.GetVersionAsync(c.VersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var ds = await db.GetDatasetAsync(v.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var known = (await db.VersionSplitsAsync([v.Id], ct)).FirstOrDefault();
        if (known?.SplitState is { } state) return new VersionSplitInfo(v, ds.Name, ds.Task, state, SplitRules.ParseCounts(known.SplitCounts));
        var counts = new Dictionary<string, long> { ["train"] = 0, ["val"] = 0, ["test"] = 0 };
        foreach (var f in await db.ListVersionFilesAsync(v.Id, ct))
            if (SplitRules.IsImage(f.Path)) counts[SplitRules.FolderSplit(f.Path) ?? "train"]++;
        var found = counts["val"] > 0 ? "folders" : "none";
        await db.SetVersionSplitAsync(v.Id, found, SplitRules.CountsJson(counts), null, ct);
        return new VersionSplitInfo(v, ds.Name, ds.Task, found, counts);
    }
}
