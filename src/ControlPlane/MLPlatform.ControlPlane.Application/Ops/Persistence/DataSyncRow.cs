using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>One finished dataset sync of an attempt into a host's dataset cache (what was reused, what was downloaded, how long).</summary>
public sealed record DataSyncRow(Guid Id, Guid JobId, Guid AttemptId, Guid HostId, Guid DatasetId, int Files, int ReusedFiles, long ReusedBytes,
    int FetchedFiles, long FetchedBytes, int UncachedFiles, int Retries, double Seconds, long CacheFiles, long CacheBytes, long FreeBytes, DateTimeOffset At);
