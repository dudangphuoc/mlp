using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>A whole dataset version a machine synced into its (still existing) cache.</summary>
public sealed record SyncedVersionRow(Guid HostId, Guid VersionId);
