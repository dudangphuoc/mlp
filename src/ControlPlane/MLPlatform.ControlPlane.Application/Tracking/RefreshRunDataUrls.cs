using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>R30: the data links in a training's job spec are signed when the job is submitted and expire after 24
/// hours.</summary>
[Access(AccessKind.Internal)]
public sealed record RefreshRunDataUrlsCommand(Guid WorkspaceId, Guid RunId, string SpecJson) : ICommand<string>, IInternalCommand;

/// <summary>Re-signs the data links of a run's job spec from the dataset version, keeping the same file set.</summary>
public sealed class RefreshRunDataUrlsCommandHandler(IReadSession db, S3Client s3) : ICommandHandler<RefreshRunDataUrlsCommand, string>
{
    public async ValueTask<string> Handle(RefreshRunDataUrlsCommand c, CancellationToken ct)
    {
        if (JsonNode.Parse(c.SpecJson) is not JsonObject spec || !Guid.TryParse(spec["datasetVersionId"]?.ToString(), out var versionId)) return c.SpecJson;
        var listKey = $"{c.WorkspaceId}/runs/{c.RunId}/data-files.json";
        HashSet<string> paths;
        if (spec["dataFiles"] is JsonArray inline) paths = inline.Select(f => f?["path"]?.ToString()).OfType<string>().ToHashSet();
        else if (spec["dataFilesUrl"] is not null && await s3.GetAsync(Buckets.Artifacts, listKey, ct) is { } stored
                 && JsonNode.Parse(stored) is JsonArray listed)
            paths = listed.Select(f => f?["path"]?.ToString()).OfType<string>().ToHashSet();
        else return c.SpecJson;

        var version = await db.GetVersionAsync(versionId, c.WorkspaceId, ct);
        if (version is null) return c.SpecJson;
        var files = (await VersionFileUrls.SignAsync(db, s3, version, ct)).Where(f => paths.Contains(f.Path))
            .Select(f => (JsonNode)f.ToJson()).ToArray();
        var list = new JsonArray(files);
        if (spec["dataFiles"] is JsonArray)
            spec["dataFiles"] = list;
        else
        {
            await s3.PutAsync(Buckets.Artifacts, listKey, System.Text.Encoding.UTF8.GetBytes(list.ToJsonString()), "application/json", ct);
            spec["dataFilesUrl"] = s3.Presign(HttpMethod.Get, Buckets.Artifacts, listKey, TimeSpan.FromDays(7), Audience.Workload);
        }
        return spec.ToJsonString();
    }
}
