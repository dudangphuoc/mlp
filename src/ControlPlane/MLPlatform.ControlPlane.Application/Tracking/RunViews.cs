using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Builds run views from run rows plus the job, dataset and user lookups.</summary>
public static class RunViews
{
    /// <summary>One lookup per module for the whole list — never one per run.</summary>
    public static async Task<List<RunView>> BuildAsync(IReadSession db, IReadOnlyList<RunRow> runs, CancellationToken ct)
    {
        if (runs.Count == 0) return [];
        var ws = runs[0].WorkspaceId;
        var jobs = (await db.JobsByIdsAsync(ws, runs.Where(r => r.JobId is not null).Select(r => r.JobId!.Value).Distinct().ToArray(), ct)).ToDictionary(j => j.Id);
        var versionIds = runs.Where(r => r.DatasetVersionId is not null).Select(r => r.DatasetVersionId!.Value).Distinct().ToArray();
        var versions = versionIds.Length == 0 ? [] : (await db.VersionNamesAsync(ws, versionIds, ct)).ToDictionary(v => v.Id);
        var users = (await db.UserNamesAsync(runs.Select(r => r.UserId).Distinct().ToArray(), ct)).ToDictionary(u => u.Id, u => u.DisplayName);
        var extras = (await db.RunExtrasAsync(ws, runs.Select(r => r.Id).ToArray(), ct)).ToDictionary(e => e.RunId);
        return runs.Select(r => new RunView(r,
            r.JobId is { } j ? jobs.GetValueOrDefault(j) : null,
            r.DatasetVersionId is { } v ? versions.GetValueOrDefault(v) : null,
            users.GetValueOrDefault(r.UserId), extras.GetValueOrDefault(r.Id))).ToList();
    }
}
