using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>Attempt holding resources for too long.</summary>
public sealed record StaleReservationRow(Guid Id, Guid JobId);
