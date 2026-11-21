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

/// <summary>Runs an eval set against a prompt version (synchronous, ≤ 100 cases) → score = share of cases passing every
/// condition; each call records `eval` usage.</summary>
[RequiresPermission("run.read")]
public sealed record RunEvalCommand(Guid WorkspaceId, Guid SetId, string PromptKey, int Version, Guid? DeploymentId) : ICommand<EvalRunRow>;

public sealed class RunEvalCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, ILlmInvoker invoker) : ICommandHandler<RunEvalCommand, EvalRunRow>
{
    public async ValueTask<EvalRunRow> Handle(RunEvalCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        var set = await db.GetEvalSetAsync(c.SetId, c.WorkspaceId, ct) ?? throw new NotFoundException("LLM_EVAL_SET_NOT_FOUND", "Eval set not found");
        var (p, v) = await PromptLookup.ResolveAsync(db, c.WorkspaceId, c.PromptKey, c.Version, ct);
        var dep = await PromptLookup.DeploymentAsync(db, c.WorkspaceId, c.DeploymentId ?? v.DeploymentId, ct);
        var results = new JsonArray();
        var passed = 0;
        var cases = JsonNode.Parse(set.Cases)!.AsArray();
        for (var i = 0; i < cases.Count; i++)
        {
            var cs = cases[i]!.AsObject();
            var vars = cs["vars"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
            var checks = new JsonObject();
            var (output, r) = await InvokeCaseAsync(dep, p, v, vars, checks, ct);
            AddExpectationChecks(cs["expect"]!.AsObject(), output, checks);
            var ok = checks.All(kv => kv.Value is not JsonValue b || !b.TryGetValue<bool>(out var x) || x);
            if (ok) passed++;
            results.Add((JsonNode)new JsonObject
            {
                ["index"] = i, ["output"] = output, ["passed"] = ok, ["checks"] = checks,
                ["tokens"] = (r?.PromptTokens ?? 0) + (r?.CompletionTokens ?? 0), ["latency_ms"] = Math.Round(r?.LatencyMs ?? 0, 1),
            });
        }
        var id = Ids.New();
        var score = Math.Round(passed / (double)Math.Max(1, cases.Count), 4);
        var now = clock.UtcNow;
        await db.InsertEvalRunAsync(id, c.WorkspaceId, set.Id, p.Id, v.Version, score, results.ToJsonString(), uid, now, ct);
        return new EvalRunRow(id, set.Id, set.Name, p.Id, p.Name, v.Version, score, results.ToJsonString(), now);
    }

    private async Task<(string Output, LlmCallResult? Result)> InvokeCaseAsync(DeploymentRow dep, PromptRow p, PromptVersionRow v,
        Dictionary<string, string> vars, JsonObject checks, CancellationToken ct)
    {
        try
        {
            var r = await LlmCall.RunAsync(db, clock, invoker, dep, PromptLookup.Messages(v, vars), v.MaxTokens, v.Temperature, "eval", p.Id, v.Version, ct);
            if (r.Blocked) checks["guardrail"] = false;
            return (r.Text, r);
        }
        catch (InvalidArgumentException e)
        {
            checks["render"] = false;
            checks["error"] = e.Message;
            return ("", null);
        }
    }

    private static void AddExpectationChecks(JsonObject expect, string output, JsonObject checks)
    {
        foreach (var (k, ev) in expect)
        {
            var want = ev?.ToString() ?? "";
            checks[k] = k switch
            {
                "contains" => output.Contains(want, StringComparison.OrdinalIgnoreCase),
                "not_contains" => !output.Contains(want, StringComparison.OrdinalIgnoreCase),
                "equals" => string.Equals(output.Trim(), want.Trim(), StringComparison.Ordinal),
                "regex" => Regex.IsMatch(output, want, RegexOptions.None, TimeSpan.FromMilliseconds(200)),
                "max_chars" => int.TryParse(want, out var n) && output.Length <= n,
                _ => false,
            };
        }
    }
}
