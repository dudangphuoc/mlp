using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>A cache volume as the janitor sees it (any workspace).</summary>
public sealed record JanitorCacheRow(Guid WorkspaceId, Guid HostId, Guid DatasetId, long CacheBytes, long FreeBytes, DateTimeOffset LastUsedAt, Guid LastJobId);
