using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Empty = MLPlatform.Contracts.V1.Empty;

namespace MLPlatform.Api.Grpc;

public sealed class InstanceGrpc(IMediator mediator) : InstanceService.InstanceServiceBase
{
    public override async Task<SetupState> GetSetupState(Empty r, ServerCallContext ctx) =>
        new() { SetupRequired = await mediator.Query(new GetSetupStateQuery(), ctx.CancellationToken) };

    public override async Task<Me> CompleteSetup(CompleteSetupRequest r, ServerCallContext ctx)
    {
        var s = await mediator.Send(new CompleteSetupCommand(r.BootstrapToken, r.Email, r.Password, r.Username,
            r.DisplayName, r.InstanceName, r.AllowSignup), ctx.CancellationToken);
        Map.SetSessionCookie(ctx, s.Token);
        return s.Me.ToProto();
    }
}
