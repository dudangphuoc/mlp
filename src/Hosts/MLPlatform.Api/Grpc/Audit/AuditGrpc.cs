using MLPlatform.ControlPlane.Infrastructure;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Audit;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Grpc;

public sealed class AuditGrpc(IMediator mediator) : AuditService.AuditServiceBase
{
    public override async Task<ListAuditEventsResponse> ListEvents(ListAuditEventsRequest r, ServerCallContext ctx)
    {
        if (!Guid.TryParse(r.WorkspaceId, out var ws)) throw new ValidationException(new Dictionary<string, string> { ["workspace_id"] = "Invalid UUID" });
        var (events, valid) = await mediator.Query(new ListAuditQuery(ws), ctx.CancellationToken);
        var res = new ListAuditEventsResponse { ChainValid = valid };
        res.Events.AddRange(events.Select(e => new AuditEvent
        {
            Seq = e.Seq, At = Timestamp.FromDateTimeOffset(e.At), ActorId = e.ActorId?.ToString() ?? "", Action = e.Action, Outcome = e.Outcome, Reason = e.Reason ?? "", Details = e.Details ?? "",
        }));
        return res;
    }
}
