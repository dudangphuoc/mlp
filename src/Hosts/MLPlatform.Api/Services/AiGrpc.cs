using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Infrastructure;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Ai;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using AiCatalogProto = MLPlatform.Contracts.V1.AiCatalog;
using AiOverviewProto = MLPlatform.Contracts.V1.AiOverview;

namespace MLPlatform.Api.Services;

/// <summary>AI providers, models, role defaults, tests and the run log (Hermes phase 1).</summary>
public sealed class AiGrpc(IMediator mediator, AiRuntime ai, AiProviders providers, SecretBox box, ICurrentUser user) : AiService.AiServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });
    private static Guid? OptG(string s) => Guid.TryParse(s, out var g) ? g : null;
    private static Timestamp? Ts(DateTimeOffset? t) => t is { } v ? Timestamp.FromDateTimeOffset(v) : null;

    private AiConnection Map(AiConnectionRow c) => new()
    {
        Id = c.Id.ToString(), Name = c.Name, ProviderType = c.ProviderType, Endpoint = c.Endpoint, HasKey = c.SecretEnc is not null,
        MaxConcurrency = c.MaxConcurrency, RpmLimit = c.RpmLimit, TimeoutMs = c.TimeoutMs, IsActive = c.IsActive, UpdatedAt = Ts(c.UpdatedAt),
        InFlight = ai.Limiter.InFlight(c.Id), IsExternal = c.IsExternal,
    };

    private AiModel Map(AiModelRow m, AiModelStatsRow? st, AiRunRow? lastTest)
    {
        var x = new AiModel
        {
            Id = m.Id.ToString(), ConnectionId = m.ConnectionId.ToString(), ModelId = m.ModelId, DisplayName = m.DisplayName, Tier = m.Tier,
            InputUsdPer1M = m.InputUsdPer1m ?? -1, OutputUsdPer1M = m.OutputUsdPer1m ?? -1, MaxTokens = m.MaxTokens, IsActive = m.IsActive,
            Health = m.Health, HealthReason = m.HealthReason ?? "", HealthAt = Ts(m.HealthAt), ConsecutiveFailures = m.ConsecutiveFailures,
            Circuit = ai.Circuits.Describe(m.Id),
            Stats = st is null ? new AiModelStats() : new AiModelStats { Runs = st.Runs, Failures = st.Failures, Fallbacks = st.Fallbacks, P50Ms = st.P50Ms, P95Ms = st.P95Ms },
        };
        x.Roles.AddRange(m.Roles); x.Inputs.AddRange(m.Inputs); x.Outputs.AddRange(m.Outputs); x.Skills.AddRange(m.Skills);
        if (lastTest is not null) x.LastTest = Map(lastTest, true);
        return x;
    }

    private static JsonNode? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonNode.Parse(json); } catch (JsonException) { return null; }
    }

    internal static AiRun Map(AiRunRow r, bool full)
    {
        var output = Parse(r.OutputJson);
        var x = new AiRun
        {
            Id = r.Id.ToString(), WorkspaceId = r.WorkspaceId?.ToString() ?? "", Kind = r.Kind, Purpose = r.Purpose, Subject = r.Subject ?? "",
            Trigger = r.Trigger, ActorName = r.ActorName ?? (r.Trigger == "manual" ? "" : "system"), State = r.State, Fallback = r.Fallback ?? "",
            ModelId = r.ModelRef?.ToString() ?? "", ModelLabel = r.ModelLabel ?? "", DecidedBy = r.DecidedBy ?? "", Confidence = r.Confidence ?? -1,
            LatencyMs = r.LatencyMs ?? 0, TokensIn = r.TokensIn ?? 0, TokensOut = r.TokensOut ?? 0, CostUsd = r.CostUsd ?? -1,
            ErrorCode = r.ErrorCode ?? "", Error = r.Error ?? "", StartedAt = Ts(r.StartedAt), FinishedAt = Ts(r.FinishedAt),
            Summary = r.State == "running" ? "Running…" : r.State == "failed" ? (r.Error ?? r.ErrorCode ?? "Failed") : output?["summary"]?.ToString() ?? "OK",
        };
        foreach (var a in Parse(r.AttemptsJson) as JsonArray ?? [])
            x.Attempts.Add(new AiAttempt
            {
                Number = a?["number"]?.GetValue<int>() ?? 0, ModelId = a?["model_id"]?.ToString() ?? "", ModelLabel = a?["label"]?.ToString() ?? "",
                Status = a?["status"]?.GetValue<int>() ?? 0, LatencyMs = a?["latency_ms"]?.GetValue<int>() ?? 0, Outcome = a?["outcome"]?.ToString() ?? "",
                Detail = a?["detail"]?.ToString() ?? "",
            });
        if (!full) return x;
        var sel = Parse(r.SelectionJson);
        x.SelectionNote = sel?["note"]?.ToString() ?? "";
        foreach (var c in sel?["candidates"] as JsonArray ?? [])
            x.Candidates.Add(new AiCandidate
            {
                ModelId = c?["model_id"]?.ToString() ?? "", ModelLabel = c?["label"]?.ToString() ?? "", Eligible = c?["eligible"]?.GetValue<bool>() ?? false,
                Reason = c?["reason"]?.ToString() ?? "", Rank = c?["rank"]?.GetValue<int>() ?? 0,
            });
        x.InputJson = r.InputJson;
        x.OutputJson = r.OutputJson;
        return x;
    }

    public override async Task<AiOverviewProto> GetOverview(GetAiOverviewRequest r, ServerCallContext ctx)
    {
        var o = await mediator.Query(new GetAiOverviewQuery(), ctx.CancellationToken);
        var stats = o.Stats.ToDictionary(s => s.ModelId);
        var res = new AiOverviewProto();
        res.Connections.AddRange(o.Connections.Select(Map));
        foreach (var m in o.Models)
        {
            AiRunRow? test = null;
            if (m.LastTestRunId is { } tid)
                try { test = await mediator.Query(new GetAiRunQuery(tid), ctx.CancellationToken); } catch (NotFoundException) { }
            res.Models.Add(Map(m, stats.GetValueOrDefault(m.Id), test));
        }
        foreach (var rd in ai.ReadinessOf(o))
        {
            var x = new AiRoleReadiness { Role = rd.Role, State = rd.State, ModelId = rd.ModelId?.ToString() ?? "", ModelLabel = rd.ModelLabel ?? "", UsedBy = rd.UsedBy };
            x.Reasons.AddRange(rd.Reasons);
            res.Readiness.Add(x);
        }
        var cat = new AiCatalogProto();
        cat.ProviderTypes.AddRange(ControlPlane.Application.Ai.AiCatalog.ProviderTypes);
        cat.Roles.AddRange(ControlPlane.Application.Ai.AiCatalog.Roles);
        cat.Inputs.AddRange(ControlPlane.Application.Ai.AiCatalog.Inputs);
        cat.Outputs.AddRange(ControlPlane.Application.Ai.AiCatalog.Outputs);
        cat.Skills.AddRange(ControlPlane.Application.Ai.AiCatalog.Skills);
        cat.Tiers.AddRange(ControlPlane.Application.Ai.AiCatalog.Tiers);
        res.Catalog = cat;
        return res;
    }

    public override async Task<AiConnection> SaveConnection(SaveAiConnectionRequest r, ServerCallContext ctx)
    {
        var key = r.ApiKey.Trim();
        var sealedKey = key.Length > 0 && key != "-" ? box.Seal(key) : null;
        AiConnectionRow row;
        if (string.IsNullOrEmpty(r.Id))
            row = await mediator.Send(new CreateAiConnectionCommand(r.Name, r.ProviderType, r.Endpoint, sealedKey, r.MaxConcurrency <= 0 ? 4 : r.MaxConcurrency,
                r.RpmLimit, r.TimeoutMs <= 0 ? 60_000 : r.TimeoutMs, r.IsExternal), ctx.CancellationToken);
        else
            row = await mediator.Send(new UpdateAiConnectionCommand(G(r.Id, "id"), r.Name, r.Endpoint, sealedKey, key == "-", r.MaxConcurrency <= 0 ? 4 : r.MaxConcurrency,
                r.RpmLimit, r.TimeoutMs <= 0 ? 60_000 : r.TimeoutMs, r.IsActive, r.IsExternal), ctx.CancellationToken);
        ai.Invalidate();
        return Map(row);
    }

    public override async Task<AiOk> DeleteConnection(DeleteAiConnectionRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new DeleteAiConnectionCommand(G(r.Id, "id")), ctx.CancellationToken);
        ai.Invalidate();
        return new AiOk { Ok = true };
    }

    public override async Task<DiscoverAiModelsResponse> DiscoverModels(DiscoverAiModelsRequest r, ServerCallContext ctx)
    {
        var o = await mediator.Query(new GetAiOverviewQuery(), ctx.CancellationToken);   // Instance Admin check
        var c = o.Connections.FirstOrDefault(x => x.Id == G(r.ConnectionId, "connection_id"))
            ?? throw new NotFoundException("AI_CONNECTION_NOT_FOUND", "Connection not found");
        var w = System.Diagnostics.Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
        cts.CancelAfter(Math.Min(c.TimeoutMs, 15_000));
        var res = new DiscoverAiModelsResponse();
        try { res.ModelIds.AddRange(await providers.ListModelsAsync(c, ai.KeyOf(c), cts.Token)); }
        catch (AiCallException e) { res.Error = e.Message; }
        catch (OperationCanceledException) when (!ctx.CancellationToken.IsCancellationRequested) { res.Error = "No answer within 15 s"; }
        res.LatencyMs = (int)w.ElapsedMilliseconds;
        return res;
    }

    public override async Task<AiModel> SaveModel(SaveAiModelRequest r, ServerCallContext ctx)
    {
        var row = await mediator.Send(new SaveAiModelCommand(OptG(r.Id), G(r.ConnectionId, "connection_id"), r.ModelId, r.DisplayName, [.. r.Roles], [.. r.Inputs],
            [.. r.Outputs], [.. r.Skills], r.Tier, r.InputUsdPer1M < 0 ? null : r.InputUsdPer1M, r.OutputUsdPer1M < 0 ? null : r.OutputUsdPer1M, r.MaxTokens, r.IsActive),
            ctx.CancellationToken);
        ai.Invalidate();
        return Map(row, null, null);
    }

    public override async Task<AiOk> DeleteModel(DeleteAiModelRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new DeleteAiModelCommand(G(r.Id, "id")), ctx.CancellationToken);
        ai.Invalidate();
        return new AiOk { Ok = true };
    }

    public override async Task<AiOk> SetRoleDefault(SetAiRoleDefaultRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new SetAiRoleDefaultCommand(r.Role, OptG(r.ModelId)), ctx.CancellationToken);
        ai.Invalidate();
        return new AiOk { Ok = true };
    }

    public override async Task<AiRun> TestModel(TestAiModelRequest r, ServerCallContext ctx)
    {
        await mediator.Query(new GetAiOverviewQuery(), ctx.CancellationToken);   // Instance Admin check
        var result = await ai.TestAsync(G(r.ModelId, "model_id"), r.Prompt, user.RequireUser(), "manual", ctx.CancellationToken);
        return Map(await mediator.Query(new GetAiRunQuery(result.RunId), ctx.CancellationToken), true);
    }

    public override async Task<ListAiRunsResponse> ListRuns(ListAiRunsRequest r, ServerCallContext ctx)
    {
        var rows = await mediator.Query(new ListAiRunsQuery(OptG(r.WorkspaceId), r.State, OptG(r.ModelId), r.Limit), ctx.CancellationToken);
        var res = new ListAiRunsResponse();
        res.Runs.AddRange(rows.Select(x => Map(x, false)));
        return res;
    }

    public override async Task<AiRun> GetRun(GetAiRunRequest r, ServerCallContext ctx) =>
        Map(await mediator.Query(new GetAiRunQuery(G(r.Id, "id")), ctx.CancellationToken), true);

    public override async Task<AiActivity> GetActivity(GetAiActivityRequest r, ServerCallContext ctx)
    {
        var ws = OptG(r.WorkspaceId);
        var s = await mediator.Query(new GetAiRunSummaryQuery(ws), ctx.CancellationToken);
        var recent = await mediator.Query(new ListAiRunsQuery(ws, "", null, 20), ctx.CancellationToken);
        var res = new AiActivity { Running = (int)s.Running, FailedRecent = (int)s.FailedRecent, FallbackRecent = (int)s.FallbackRecent, LastAt = Ts(s.LastAt) };
        res.Recent.AddRange(recent.Select(x => Map(x, false)));
        return res;
    }

    public override async Task<ListAiAuditResponse> ListAudit(ListAiAuditRequest r, ServerCallContext ctx)
    {
        var rows = await mediator.Query(new ListAiAuditQuery(r.Limit), ctx.CancellationToken);
        var res = new ListAiAuditResponse();
        res.Entries.AddRange(rows.Select(a => new AiAuditEntry
        {
            Id = a.Id.ToString(), ActorName = a.ActorName, Action = a.Action, Target = a.Target, BeforeJson = a.BeforeJson ?? "", AfterJson = a.AfterJson ?? "", At = Ts(a.At),
        }));
        return res;
    }
}
