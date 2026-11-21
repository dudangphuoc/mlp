using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Serving;

/// <summary>
/// Model files of a version as [{path, url}]: a reference version (imported by address) is read from its sources, otherwise from our storage.
/// Links last 24 hours, so the scheduler signs them again for every replica start.
/// </summary>
public static class ModelFiles
{
    public static async Task<string> BuildAsync(IReadSession db, S3Client s3, Guid versionId, Guid workspaceId, CancellationToken ct)
    {
        var refs = await db.ListVersionFileRefsAsync(versionId, ct);
        if (refs.Count > 0)
            return new JsonArray(refs.Select(f => (JsonNode)new JsonObject { ["path"] = "weights/" + f.Path, ["url"] = SourceUrls.Resolve(s3, f.Url!) }).ToArray()).ToJsonString();
        var prefix = await db.GetVersionPrefixAsync(versionId, workspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        return new JsonArray((await s3.ListAsync(Buckets.Artifacts, prefix, ct)).Select(f => (JsonNode)new JsonObject
        {
            ["path"] = f.Key[prefix.Length..],
            ["url"] = s3.Presign(HttpMethod.Get, Buckets.Artifacts, f.Key, TimeSpan.FromHours(24), Audience.Workload),
        }).ToArray()).ToJsonString();
    }
}
