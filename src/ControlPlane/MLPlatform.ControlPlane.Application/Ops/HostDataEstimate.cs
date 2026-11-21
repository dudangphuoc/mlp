using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Before a run: per machine, how much of a dataset version is already there and how much would be downloaded.</summary>
public sealed record HostDataEstimate(Guid HostId, long Files, long Bytes, long CachedFiles, long CachedBytes, long FreeBytes, bool Known);
