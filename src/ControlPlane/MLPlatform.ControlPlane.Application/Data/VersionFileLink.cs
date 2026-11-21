using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>One file of a version for a job: signed link, size, and the cache id (null = the bytes behind the address
/// may change → never cached).</summary>
public sealed record VersionFileLink(string Path, string Url, long Size, string? Id)
{
    public System.Text.Json.Nodes.JsonObject ToJson()
    {
        var o = new System.Text.Json.Nodes.JsonObject { ["path"] = Path, ["url"] = Url, ["size"] = Size };
        if (Id is not null) o["id"] = Id;
        return o;
    }
}
