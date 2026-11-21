using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Records an operational event row (20/10 §2.1) — in the same transaction as the state change that caused it.</summary>
/// <summary>
/// Authenticates the `mlpj_` workload token with fencing (20/10 §4.5): a token of a replaced/finished attempt → `SCHED_ATTEMPT_FENCED`,
/// so two copies never write to the same run.
/// </summary>
public static class Workload
{
    public static async Task<WorkloadAttemptRow> ResolveAsync(IReadSession db, TokenService tokens, string token, CancellationToken ct)
    {
        var a = string.IsNullOrEmpty(token) ? null : await db.AttemptByWorkloadTokenAsync(tokens.Hash(token), ct);
        if (a is null) throw new UnauthenticatedException("SCHED_TOKEN_INVALID", "Invalid job token");
        if (a.State is not ("launching" or "running" or "suspect"))
            throw new PreconditionException("SCHED_ATTEMPT_FENCED", "This run attempt has been replaced by a newer one");
        return a;
    }
}
