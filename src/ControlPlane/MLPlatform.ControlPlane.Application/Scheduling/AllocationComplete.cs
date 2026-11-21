using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed record AllocationComplete(AttemptRow Attempt, JobSpecDto Spec, IReadOnlyList<AllocationRow> Allocations);
