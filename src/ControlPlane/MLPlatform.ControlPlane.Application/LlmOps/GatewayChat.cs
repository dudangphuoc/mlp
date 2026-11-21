using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.LlmOps;

/// <summary>REST gateway `{"messages":[…]}`: same guardrails + usage recording as prompt calls (no user, no audit).</summary>
public sealed record GatewayChatCommand(DeploymentRow Deployment, IReadOnlyList<(string Role, string Content)> Messages, int MaxTokens, double Temperature)
    : ICommand<LlmCallResult>, IInternalCommand;

public sealed class GatewayChatCommandHandler(IWriteSession db, IClock clock, ILlmInvoker invoker) : ICommandHandler<GatewayChatCommand, LlmCallResult>
{
    public async ValueTask<LlmCallResult> Handle(GatewayChatCommand c, CancellationToken ct) =>
        await LlmCall.RunAsync(db, clock, invoker, c.Deployment, c.Messages, c.MaxTokens > 0 ? c.MaxTokens : 512, c.Temperature, "gateway", null, null, ct);
}
