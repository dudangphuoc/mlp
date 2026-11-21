using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>The attempt was already finalized (replaced, node-lost, fenced): its late exit must not change the job,
/// run, deployment or notebook.</summary>
public sealed record FinishResult(Guid JobId, string JobState, bool Retry, bool Stale = false);
