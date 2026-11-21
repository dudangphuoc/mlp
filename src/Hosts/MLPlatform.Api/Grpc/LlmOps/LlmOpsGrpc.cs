using System.Text.Json.Nodes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.LlmOps;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Grpc;

public sealed class LlmOpsGrpc(IMediator mediator) : LlmOpsService.LlmOpsServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });
    private static Guid? OptG(string s, string f) => string.IsNullOrEmpty(s) ? null : G(s, f);
    private static Timestamp? Ts(DateTimeOffset? t) => t is { } v && v > DateTimeOffset.MinValue ? Timestamp.FromDateTimeOffset(v) : null;

    public override async Task<Prompt> CreatePrompt(CreatePromptRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreatePromptCommand(G(r.WorkspaceId, "workspace_id"), r.Name, r.Description), ctx.CancellationToken));

    public override async Task<ListPromptsResponse> ListPrompts(ListPromptsRequest r, ServerCallContext ctx)
    {
        var res = new ListPromptsResponse();
        res.Prompts.AddRange((await mediator.Query(new ListPromptsQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<PromptDetail> GetPrompt(PromptRef r, ServerCallContext ctx)
    {
        var (p, vs) = await mediator.Query(new GetPromptQuery(G(r.WorkspaceId, "workspace_id"), r.Prompt), ctx.CancellationToken);
        var res = new PromptDetail { Prompt = ToProto(p) };
        res.Versions.AddRange(vs.Select(ToProto));
        return res;
    }

    public override async Task<PromptVersion> CreatePromptVersion(CreatePromptVersionRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreatePromptVersionCommand(G(r.WorkspaceId, "workspace_id"), r.Prompt, r.System, r.Template,
            OptG(r.DeploymentId, "deployment_id"), r.Temperature, r.MaxTokens, r.Note), ctx.CancellationToken));

    public override async Task<LlmCallResponse> RunPrompt(RunPromptRequest r, ServerCallContext ctx)
    {
        var (v, res) = await mediator.Send(new RunPromptCommand(G(r.WorkspaceId, "workspace_id"), r.Prompt, r.Version,
            r.Variables.ToDictionary(kv => kv.Key, kv => kv.Value), OptG(r.DeploymentId, "deployment_id")), ctx.CancellationToken);
        var p = ToProto(res);
        p.Version = v.Version;
        return p;
    }

    public override async Task<LlmSettings> GetGuardrails(DeploymentLlmRef r, ServerCallContext ctx) =>
        ToProto(r.WorkspaceId, await mediator.Query(new GetLlmSettingsQuery(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id")), ctx.CancellationToken));

    public override async Task<LlmSettings> UpdateGuardrails(LlmSettings r, ServerCallContext ctx) =>
        ToProto(r.WorkspaceId, await mediator.Send(new UpdateLlmSettingsCommand(G(r.WorkspaceId, "workspace_id"), G(r.DeploymentId, "deployment_id"), r.PiiMode,
            r.BlockInjection, r.BlockedTerms.ToList(), r.CheckOutput, r.PricePer1K), ctx.CancellationToken));

    public override async Task<LlmUsage> GetUsage(GetLlmUsageRequest r, ServerCallContext ctx)
    {
        var res = new LlmUsage();
        res.Days.AddRange((await mediator.Query(new GetLlmUsageQuery(G(r.WorkspaceId, "workspace_id"), r.Days), ctx.CancellationToken)).Select(d => new LlmUsageDay
        {
            Day = Timestamp.FromDateTimeOffset(d.Day), DeploymentId = d.DeploymentId.ToString(), Deployment = d.Slug, Calls = d.Calls, PromptTokens = d.PromptTokens,
            CompletionTokens = d.CompletionTokens, Cost = d.Cost, AvgLatencyMs = d.AvgLatencyMs, Blocked = d.Blocked,
        }));
        return res;
    }

    public override async Task<EvalSet> CreateEvalSet(CreateEvalSetRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreateEvalSetCommand(G(r.WorkspaceId, "workspace_id"), r.Name, r.CasesJson), ctx.CancellationToken));

    public override async Task<ListEvalSetsResponse> ListEvalSets(ListEvalSetsRequest r, ServerCallContext ctx)
    {
        var res = new ListEvalSetsResponse();
        res.Sets.AddRange((await mediator.Query(new ListEvalSetsQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<EvalRun> RunEval(RunEvalRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new RunEvalCommand(G(r.WorkspaceId, "workspace_id"), G(r.SetId, "set_id"), r.Prompt, r.Version,
            OptG(r.DeploymentId, "deployment_id")), ctx.CancellationToken));

    public override async Task<ListEvalRunsResponse> ListEvalRuns(ListEvalRunsRequest r, ServerCallContext ctx)
    {
        var res = new ListEvalRunsResponse();
        res.Runs.AddRange((await mediator.Query(new ListEvalRunsQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    private static Prompt ToProto(PromptRow p) => new() { Id = p.Id.ToString(), Name = p.Name, Description = p.Description, LatestVersion = p.LatestVersion, CreatedAt = Ts(p.CreatedAt) };

    private static PromptVersion ToProto(PromptVersionRow v)
    {
        var p = new PromptVersion { Version = v.Version, System = v.System, Template = v.Template, DeploymentId = v.DeploymentId?.ToString() ?? "",
            Temperature = v.Temperature, MaxTokens = v.MaxTokens, Note = v.Note, CreatedAt = Ts(v.CreatedAt) };
        p.Variables.AddRange(JsonNode.Parse(v.Variables)!.AsArray().Select(x => x!.ToString()));
        return p;
    }

    public static LlmCallResponse ToProto(LlmCallResult r)
    {
        var p = new LlmCallResponse { Output = r.Text, PromptTokens = r.PromptTokens, CompletionTokens = r.CompletionTokens, LatencyMs = r.LatencyMs,
            Cost = r.Cost, Blocked = r.Blocked, BlockedReason = r.Reason ?? "" };
        p.Flags.AddRange(r.Flags);
        p.SentMessages.AddRange(r.SentMessages.Select(m => $"{m.Role}: {m.Content}"));
        return p;
    }

    private static LlmSettings ToProto(string ws, LlmSettingsRow s)
    {
        var p = new LlmSettings { WorkspaceId = ws, DeploymentId = s.DeploymentId.ToString(), PiiMode = s.PiiMode, BlockInjection = s.BlockInjection,
            CheckOutput = s.CheckOutput, PricePer1K = s.PricePer1k };
        p.BlockedTerms.AddRange(JsonNode.Parse(s.BlockedTerms)!.AsArray().Select(x => x!.ToString()));
        return p;
    }

    private static EvalSet ToProto(EvalSetRow s) => new() { Id = s.Id.ToString(), Name = s.Name, CasesJson = s.Cases, CreatedAt = Ts(s.CreatedAt) };

    private static EvalRun ToProto(EvalRunRow r) => new()
    {
        Id = r.Id.ToString(), SetId = r.SetId.ToString(), SetName = r.SetName, Prompt = r.PromptName, PromptVersion = r.PromptVersion, Score = r.Score,
        ResultsJson = r.Results, CreatedAt = Ts(r.CreatedAt),
    };
}
