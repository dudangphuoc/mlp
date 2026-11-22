using System.Security.Cryptography;
using System.Text;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Audit;

public sealed record AuditRow(long Seq, Guid Id, DateTimeOffset At, Guid? WorkspaceId, Guid? ActorId, string Action, string Outcome, string? Reason,
    string? Details, string PrevHash, string Hash);
