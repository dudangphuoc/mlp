using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

public sealed record StoppingServerRow(Guid Id, Guid? JobId, string JobState, DateTimeOffset StateAt, string? StateReason);
