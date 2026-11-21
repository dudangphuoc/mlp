using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>How much of one dataset a machine holds in its cache.</summary>
public sealed record HostCacheRow(Guid HostId, long CacheBytes, long CacheFiles);
