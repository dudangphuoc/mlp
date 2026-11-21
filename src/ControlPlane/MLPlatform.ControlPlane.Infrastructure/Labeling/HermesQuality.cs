using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

// Hermes phase 4.
public sealed class HermesQuality(AiRuntime ai, ItemContent content)
{
    public const int MinReviewed = 10;
    public const double MaxErrorRate = 0.02;

    public sealed record Analyst(string State, string Model, bool External, IReadOnlyList<string> Reasons);

    public async Task<Analyst> AnalystAsync(CancellationToken ct)
    {
        var cat = await ai.CatalogAsync(ct);
        var r = ai.ReadinessOf(cat).First(x => x.Role == "analyst");
        var external = r.ModelId is { } mid && cat.Models.FirstOrDefault(m => m.Id == mid) is { } m && cat.Connections.First(c => c.Id == m.ConnectionId).IsExternal;
        return new Analyst(r.State, r.ModelLabel ?? "", external, r.Reasons);
    }

    private async Task GuardAsync(IMediator mediator, Guid ws, Guid pid, bool confirm, CancellationToken ct)
    {
        var a = await AnalystAsync(ct);
        if (a.State == "missing") throw new PreconditionException("AI_NO_ANALYST", "No analyst model is ready (AI models page): " + string.Join("; ", a.Reasons));
        if (!a.External) return;
        var (_, settings, _) = await mediator.Query(new GetAssistSettingsQuery(ws, pid), ct);
        if (!settings.AllowExternal) throw new PreconditionException("LABEL_EXTERNAL_OFF", $"The analyst model ({a.Model}) is an external API and external APIs are off for this project (AI assist tab)");
        if (!confirm) throw new PreconditionException("LABEL_EXTERNAL_CONFIRM", "Sending this to an external API needs an explicit confirmation");
    }

    private static string Language(string lang) => lang == "vi" ? "Vietnamese" : "English";

    // ───────── explanation ─────────

    public async Task<HermesCheckRow> ExplainAsync(IMediator mediator, Guid ws, Guid pid, Guid checkId, string lang, bool confirm, CancellationToken ct)
    {
        var check = await mediator.Query(new GetHermesCheckQuery(ws, pid, checkId), ct);
        await GuardAsync(mediator, ws, pid, confirm, ct);
        var ctx = await mediator.Query(new GetHermesContextQuery(ws, pid, check.Path), ct);
        var p = ctx.Project;
        var prompt = await ExplainPromptAsync(check, ctx, lang, ct);

        var r = await ai.ChatAsync(new AiCallSpec("chat", "analyst", "Explain a label check", $"{p.Name} · {check.Path}", "manual", p.WorkspaceId, ctx.ActorId,
            DeadlineMs: 90_000),
            [new AiMessage("system", "You are Hermes, the labeling quality assistant of MLPlatform. Be brief, concrete and kind."), new AiMessage("user", prompt)], ct);
        if (!r.Ok || string.IsNullOrWhiteSpace(r.Text))
            throw new UnavailableException("AI_EXPLAIN_FAILED", $"The analyst model did not answer ({r.ErrorCode}): {r.Error}. The AI log has the full call.");
        await mediator.Send(new SetHermesExplanationCommand(ws, check.Id, r.Text.Trim(), lang, r.RunId), CancellationToken.None);
        return await mediator.Query(new GetHermesCheckQuery(ws, pid, check.Id), CancellationToken.None);
    }

    private static Dictionary<int, string> ClassNames(string json)
    {
        var classes = new Dictionary<int, string>();
        try { foreach (var c in JsonNode.Parse(json) as JsonArray ?? []) if (c?["id"]?.GetValue<int>() is { } id) classes[id] = c["name"]?.ToString() ?? ""; }
        catch (System.Text.Json.JsonException) { }
        return classes;
    }

    private async Task<string> ExplainPromptAsync(HermesCheckRow check, HermesContext ctx, string lang, CancellationToken ct)
    {
        var p = ctx.Project;
        var classes = ClassNames(ctx.Classes);
        var sb = new StringBuilder();
        sb.Append("Project: ").Append(p.Name).Append('\n');
        if (!string.IsNullOrWhiteSpace(p.GuidelineMd)) sb.Append("Guideline:\n").Append(AiProviders.Clip(p.GuidelineMd.Trim(), 1500)).Append('\n');
        sb.Append("Classes: ").Append(string.Join(", ", classes.Values)).Append('\n');
        if (HermesLabelChecker.Media(check.Path) == "text")
        {
            var (text, _) = await content.ReadTextAsync(ctx.Source, 4000, ct);
            sb.Append("Item text:\n\"\"\"\n").Append(text ?? "(could not be read)").Append("\n\"\"\"\n");
        }
        else sb.Append("Item: ").Append(HermesLabelChecker.Media(check.Path)).Append(" file (its content is not sent; only the labels and the check steps are)\n");
        AppendLabels(sb, ctx.Annotations, classes);
        AppendCheck(sb, check);
        sb.Append($"\nExplain to the annotator in {Language(lang)}, in at most 5 short bullet points, why the check concluded \"{check.Verdict}\" and exactly what to change ")
          .Append("(or, if it passed, what makes the labels right). Only use the item, the labels and the steps above; do not invent facts.");
        return sb.ToString();
    }

    private static void AppendLabels(StringBuilder sb, List<AnnotationRow> annotations, Dictionary<int, string> classes)
    {
        sb.Append("Labels given by the annotator:\n");
        foreach (var a in annotations)
        {
            string? quote = null;
            try { quote = JsonNode.Parse(a.Geometry)?["text"]?.ToString(); } catch (System.Text.Json.JsonException) { }
            sb.Append("- ").Append(a.Kind).Append(": ").Append(classes.GetValueOrDefault(a.ClassId, a.ClassId.ToString(CultureInfo.InvariantCulture)))
              .Append(quote is null ? "" : $" (\"{AiProviders.Clip(quote, 120)}\")").Append(a.Source == "model" ? " [proposed by AI]" : "").Append('\n');
        }
        if (annotations.Count == 0) sb.Append("(none)\n");
    }

    private static void AppendCheck(StringBuilder sb, HermesCheckRow check)
    {
        sb.Append("Hermes check: verdict ").Append(check.Verdict).Append(check.Confidence is { } cf ? $", model confidence {cf:0.00}" : "").Append('\n');
        try
        {
            foreach (var s in JsonNode.Parse(check.StepsJson) as JsonArray ?? [])
                sb.Append("- ").Append(s?["name"]).Append(": ").Append(s?["outcome"]).Append(" — ").Append(s?["detail"]).Append('\n');
        }
        catch (System.Text.Json.JsonException) { }
        if (!string.IsNullOrEmpty(check.Suggestion)) sb.Append("Suggested class: ").Append(check.Suggestion).Append('\n');
    }

    // ───────── calibration ─────────

    private static bool Agreed(CalibrationRow r) => r.Verdict == "pass" ? r.Outcome == "accept" : r.Outcome is "reject" or "reopen" or "hermes.undo";

    public static Calibration Calibrate(List<CalibrationRow> rows, double currentThreshold)
    {
        var c = new Calibration { Checks = rows.Count, CurrentThreshold = currentThreshold, Recommended = -1 };
        var reviewed = rows.Where(r => r.Outcome != "").ToList();
        c.Reviewed = reviewed.Count;
        var passRev = reviewed.Where(r => r.Verdict == "pass").ToList();
        c.PassReviewed = passRev.Count;
        c.PassDisagreed = passRev.Count(r => !Agreed(r));
        var failRev = reviewed.Where(r => r.Verdict != "pass").ToList();
        c.FailReviewed = failRev.Count;
        c.FailAgreed = failRev.Count(Agreed);
        c.AutoUnreviewed = rows.Count(r => r.Action == "auto_accepted" && r.Outcome == "");

        foreach (var (lo, hi) in new[] { (0.0, 0.5), (0.5, 0.7), (0.7, 0.8), (0.8, 0.9), (0.9, 0.95), (0.95, 1.0001) })
        {
            var inB = passRev.Where(r => r.Confidence is { } x && x >= lo && x < hi).ToList();
            c.Buckets.Add(new CalibrationBucket { Lo = lo, Hi = Math.Min(hi, 1), Reviewed = inB.Count, HumanAgreed = inB.Count(Agreed) });
        }
        var passAll = rows.Where(r => r.Verdict == "pass" && r.Confidence is not null).ToList();
        foreach (var t in new[] { 0.6, 0.7, 0.8, 0.85, 0.9, 0.95, 0.99 })
        {
            var rev = passRev.Where(r => r.Confidence >= t).ToList();
            var bad = rev.Count(r => !Agreed(r));
            c.Options.Add(new ThresholdOption
            {
                Threshold = t, WouldAutoAccept = passAll.Count(r => r.Confidence >= t), Reviewed = rev.Count, Disagreed = bad,
                ErrorRate = rev.Count == 0 ? -1 : (double)bad / rev.Count,
            });
        }
        var pick = c.Options.FirstOrDefault(o => o.Reviewed >= MinReviewed && o.ErrorRate >= 0 && o.ErrorRate <= MaxErrorRate);
        if (pick is not null)
        {
            c.Recommended = pick.Threshold;
            c.Recommendation = $"At ≥ {pick.Threshold:P0}, people disagreed with {pick.Disagreed} of {pick.Reviewed} reviewed passing checks ({pick.ErrorRate:P1}); limit {MaxErrorRate:P0}";
        }
        else c.Recommendation = c.PassReviewed < MinReviewed
            ? $"Not enough evidence yet: {c.PassReviewed} passing checks were decided by a person; at least {MinReviewed} are needed"
            : $"No threshold keeps disagreement within {MaxErrorRate:P0} on the reviewed data — keep a reviewer on every item";
        return c;
    }

    // ───────── summary ─────────

    public async Task<QualitySummary> SummarizeAsync(IMediator mediator, Guid ws, Guid pid, string lang, bool confirm, Guid actor, CancellationToken ct)
    {
        var report = await mediator.Query(new GetQualityReportQuery(ws, pid), ct);
        if (!report.CanManage) throw new ForbiddenException("LABEL_NOT_MEMBER", "Only a Lead can generate the AI quality summary");
        await GuardAsync(mediator, ws, pid, confirm, ct);
        var cal = Calibrate(report.Calibration, report.Project.AutoAcceptThreshold);
        var legend = new Dictionary<string, string>();
        var sent = SummaryData(report, cal, legend).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var r = await ai.ChatAsync(new AiCallSpec("chat", "analyst", "Quality summary", report.Project.Name, "manual", ws, actor, DeadlineMs: 90_000),
        [
            new AiMessage("system", "You are Hermes, the labeling quality assistant of MLPlatform. You only use the numbers given; never guess missing ones."),
            new AiMessage("user", $"Write a short quality report in {Language(lang)} for the project Lead: 3–6 bullet points on how labeling is going, " +
                "which annotators (by their alias A1, A2…) need help and why, whether automatic acceptance is safe at the current threshold, and one concrete next step. " +
                "Data (JSON):\n" + sent),
        ], ct);
        if (!r.Ok || string.IsNullOrWhiteSpace(r.Text))
            throw new UnavailableException("AI_SUMMARY_FAILED", $"The analyst model did not answer ({r.ErrorCode}): {r.Error}. The AI log has the full call.");
        var a2 = await AnalystAsync(ct);
        var res = new QualitySummary { Text = r.Text.Trim(), AiRunId = r.RunId.ToString(), Model = r.ModelLabel ?? a2.Model, External = a2.External, SentJson = sent };
        foreach (var (k, v) in legend) res.Legend[k] = v;
        return res;
    }

    /// <summary>Builds the anonymized summary input; fills the alias legend as it goes.</summary>
    private static JsonObject SummaryData(LabelQualityReport report, Calibration cal, Dictionary<string, string> legend)
    {
        var people = new JsonArray();
        var i = 0;
        foreach (var a in report.Annotators)
        {
            var alias = "A" + (++i).ToString(CultureInfo.InvariantCulture);
            legend[alias] = a.AnnotatorName ?? a.AnnotatorId.ToString();
            people.Add((JsonNode)new JsonObject
            {
                ["annotator"] = alias, ["items"] = a.Items, ["submitted"] = a.Submitted, ["accepted"] = a.Accepted, ["rejected"] = a.Rejected,
                ["returned_ever"] = a.ReturnedEver, ["hermes_pass"] = a.HermesPass, ["hermes_flag"] = a.HermesFlag, ["hermes_fail"] = a.HermesFail,
                ["auto_accepted"] = a.AutoAccepted, ["undone"] = a.Undone,
            });
        }
        var data = new JsonObject
        {
            ["project"] = report.Project.Name, ["review_mode"] = report.Project.ReviewMode, ["auto_accept_threshold"] = report.Project.AutoAcceptThreshold,
            ["annotators"] = people,
            ["calibration"] = new JsonObject
            {
                ["checks"] = cal.Checks, ["reviewed"] = cal.Reviewed, ["pass_reviewed"] = cal.PassReviewed, ["pass_disagreed"] = cal.PassDisagreed,
                ["fail_reviewed"] = cal.FailReviewed, ["fail_agreed"] = cal.FailAgreed, ["auto_accepted_unreviewed"] = cal.AutoUnreviewed,
                ["recommended_threshold"] = cal.Recommended < 0 ? null : cal.Recommended, ["recommendation"] = cal.Recommendation,
            },
        };
        return data;
    }
}
