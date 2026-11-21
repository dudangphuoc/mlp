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

/// <summary>One guarded LLM call + usage recording (tokens, latency, estimated cost). When blocked the model is not called, but `blocked` usage is still recorded.</summary>
public static class LlmCall
{
    public static async Task<LlmCallResult> RunAsync(IWriteSession db, IClock clock, ILlmInvoker invoker, DeploymentRow dep,
        IReadOnlyList<(string Role, string Content)> messages, int maxTokens, double temperature, string source, Guid? promptId, int? promptVersion, CancellationToken ct)
    {
        if (dep.State != "healthy" || dep.Endpoint is null) throw new UnavailableException("SERVE_NOT_READY", $"Deployment {dep.Slug} is {dep.State}");
        var settings = await db.GetLlmSettingsAsync(dep.Id, ct);
        var flags = new List<string>();
        var sent = new List<(string, string)>();
        foreach (var (role, content) in messages)
        {
            var g = Guardrails.Apply(content, settings, output: false);
            flags.AddRange(g.Flags);
            if (g.Blocked) return await Record(db, clock, dep, settings, new LlmCallResult("", 0, 0, 0, 0, true, g.Reason, flags.Distinct().ToList(), sent), source, promptId, promptVersion, ct);
            sent.Add((role, g.Text));
        }
        // Do not hold a pooled connection (or an open transaction) while waiting for the model.
        await db.ReleaseAsync(ct);
        var sw = Stopwatch.StartNew();
        var (text, pt, comp) = await invoker.ChatAsync(dep.Endpoint, sent, maxTokens, temperature, ct);
        var latency = sw.Elapsed.TotalMilliseconds;
        // Model does not report tokens → estimate by whitespace (good enough for observation; the real tokenizer belongs to the recipe).
        if (pt == 0) pt = sent.Sum(m => Words(m.Item2));
        if (comp == 0) comp = Words(text);
        var blocked = false;
        string? reason = null;
        if (settings?.CheckOutput == true)
        {
            var g = Guardrails.Apply(text, settings, output: true);
            flags.AddRange(g.Flags.Select(f => "output:" + f));
            (text, blocked, reason) = g.Blocked ? ("", true, "Output: " + g.Reason) : (g.Text, false, null);
        }
        var cost = (pt + comp) / 1000.0 * (settings?.PricePer1k ?? 0);
        return await Record(db, clock, dep, settings, new LlmCallResult(text, pt, comp, latency, cost, blocked, reason, flags.Distinct().ToList(), sent), source, promptId, promptVersion, ct);
    }

    private static async Task<LlmCallResult> Record(IWriteSession db, IClock clock, DeploymentRow dep, LlmSettingsRow? s, LlmCallResult r, string source,
        Guid? promptId, int? promptVersion, CancellationToken ct)
    {
        await db.InsertUsageAsync(Ids.New(), dep.WorkspaceId, dep.Id, promptId, promptVersion, source, clock.UtcNow, r.PromptTokens, r.CompletionTokens,
            r.LatencyMs, r.Cost, r.Blocked, new JsonArray(r.Flags.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()).ToJsonString(), ct);
        _ = s;
        return r;
    }

    private static int Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
