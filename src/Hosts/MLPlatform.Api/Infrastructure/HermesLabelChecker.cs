using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.Cqrs;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.Api.Infrastructure;

/*
 * Hermes label check (phase 2). Fast and visible:
 *   1. rules (instant): labels present, classes valid, shapes plausible, duplicates;
 *   2. decision model (≈ 0.3 s, typed answer with confidence) — for text items, whose content the model can read;
 *   3. auto-accept — only on submit, only when the project threshold is set, the verdict passes and the model is confident enough.
 * Every step is stored with its outcome, detail and time; the model call is an AI run anyone can open. Nothing is decided silently.
 */
public sealed class HermesLabelChecker(AiRuntime ai, ItemContent content, ILogger<HermesLabelChecker> log)
{
    private static readonly string[] TextExt = ["txt", "md", "json", "jsonl", "html", "xml", "csv", "tsv"];

    private sealed record Step(string Name, string Kind, string Outcome, string Detail, int Ms);
    private sealed record Cls(int Id, string Name);

    public static string Media(string path)
    {
        var e = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return TextExt.Contains(e) ? "text"
            : e is "wav" or "mp3" or "flac" or "ogg" or "m4a" or "aac" or "opus" ? "audio"
            : e is "mp4" or "webm" or "mov" or "mkv" or "avi" ? "video" : "image";
    }

    /// <summary>Runs a check. trigger = submit (automatic, may auto-accept) | manual (never changes the item). Returns null when Hermes is off for an automatic check.</summary>
    public async Task<HermesCheckRow?> CheckAsync(IMediator mediator, Guid ws, Guid projectId, string path, string trigger, CancellationToken ct)
    {
        var ctx = await mediator.Query(new GetHermesContextQuery(ws, projectId, path), ct);
        var p = ctx.Project;
        if (trigger == "submit" && !p.HermesEnabled) return null;

        var steps = new List<Step>();
        var id = Ids.New();
        Guid? runId = null;
        double? confidence = null;
        string? suggestion = null;
        var decidedBy = "rules";
        string verdict;
        try
        {
            var classes = ParseClasses(ctx.Classes);
            var anns = ctx.Annotations;
            RunRules(anns, classes, steps);

            var media = Media(path);
            var w = Stopwatch.StartNew();
            var cat = await ai.CatalogAsync(ct);
            var ready = ai.ReadinessOf(cat).First(r => r.Role == "decision");
            if (anns.Count == 0)
                steps.Add(new("Decision model", "model", "skipped", "Not asked: there are no labels to judge", 0));
            else if (ready.State == "missing")
                steps.Add(new("Decision model", "model", "skipped", "No decision model is ready: " + string.Join("; ", ready.Reasons), 0));
            else if (media != "text")
                steps.Add(new("Decision model", "model", "skipped",
                    $"The decision model reads text only; {media} content is not sent to it. Vision checks come with label proposals (phase 3). The verdict uses rules only.", 0));
            else
            {
                var model = cat.Models.First(m => m.Id == ready.ModelId).ModelId;
                var budgetChars = AiProviders.StateTokenLimit(model) * 3 - 3000;
                var (text, note) = await content.ReadTextAsync(ctx.Source, budgetChars - Math.Min(1500, p.GuidelineMd.Length), ct);
                if (text is null)
                    steps.Add(new("Item text", "rule", "error", "Could not read the item text: " + note, (int)w.ElapsedMilliseconds));
                else
                {
                    steps.Add(new("Item text", "rule", "pass", note ?? $"{text.Length:N0} characters sent to the model", (int)w.ElapsedMilliseconds));
                    w.Restart();
                    var tags = anns.Where(a => a.Kind == "tag").Select(a => a.ClassId).Distinct().ToList();
                    var state = BuildState(p, classes, text, anns);
                    var questions = new List<AiQuestion>
                    {
                        new("labels_ok", "choice", "Do the annotator's labels match the item text and the guideline?",
                        [
                            new("correct", "The labels match the text and the guideline"),
                            new("wrong", "At least one label contradicts the text or the guideline"),
                            new("unsure", "The text does not allow a confident decision"),
                        ]),
                    };
                    if (tags.Count > 0 && classes.Count is >= 2 and <= 30)
                        questions.Add(new("best_class", "choice", "Which single class fits the item best?",
                            [.. classes.Select(c => new AiOption("c" + c.Id.ToString(CultureInfo.InvariantCulture), c.Name))]));
                    var r = await ai.DecideAsync(new AiCallSpec("decision", "decision", "Label check", $"{p.Name} · {path}", trigger == "submit" ? "auto" : "manual",
                        p.WorkspaceId, ctx.ActorId, DeadlineMs: 4000), state, questions, null, ct);
                    runId = r.RunId;
                    var ms = (int)w.ElapsedMilliseconds;
                    if (!r.Ok || r.Answers is null)
                        steps.Add(new("Decision model", "model", "error", $"No answer ({r.ErrorCode}): {r.Error}. The verdict uses rules only.", ms));
                    else
                    {
                        decidedBy = "rules+model";
                        var ok = r.Answers["labels_ok"];
                        var choice = ok?["choice"]?.ToString();
                        double? c = ok?["confidence"] is JsonValue cv && cv.TryGetValue<double>(out var cd) ? cd : null;
                        var pct = c is { } x ? x.ToString("P0", CultureInfo.InvariantCulture) : "?";
                        var by = r.ModelLabel ?? "?";
                        if (choice == "correct") { confidence = c; steps.Add(new("Decision model", "model", "pass", $"Labels match the text ({pct} sure) — {by}", ms)); }
                        else if (choice == "wrong") { confidence = c; steps.Add(new("Decision model", "model", "fail", $"Labels contradict the text ({pct} sure) — {by}", ms)); }
                        else steps.Add(new("Decision model", "model", "flag", $"The model is unsure ({pct} sure) — {by}", ms));

                        if (r.Answers["best_class"]?["choice"]?.ToString() is { } best && int.TryParse(best.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bestId))
                        {
                            var name = classes.FirstOrDefault(k => k.Id == bestId)?.Name ?? best;
                            double? bc = r.Answers["best_class"]?["confidence"] is JsonValue bv && bv.TryGetValue<double>(out var bd) ? bd : null;
                            var bpct = bc is { } y ? y.ToString("P0", CultureInfo.InvariantCulture) : "?";
                            if (tags.Contains(bestId)) steps.Add(new("Best class", "model", "pass", $"The model picks the same class: {name} ({bpct})", 0));
                            else
                            {
                                suggestion = name;
                                var given = string.Join(", ", tags.Select(t => classes.FirstOrDefault(k => k.Id == t)?.Name ?? t.ToString(CultureInfo.InvariantCulture)));
                                steps.Add(new("Best class", "model", "flag", $"The model would pick {name} ({bpct}) instead of {given}", 0));
                            }
                        }
                    }
                }
            }

            verdict = steps.Any(s => s.Outcome == "fail") ? "fail" : steps.Any(s => s.Outcome is "flag") ? "flag" : "pass";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Hermes check of {Path} failed", path);
            steps.Add(new("Check", "rule", "error", "The check stopped unexpectedly: " + e.Message, 0));
            verdict = "error";
        }

        // Auto-accept (submit only). Every reason it does or does not happen is a visible step.
        var action = "none";
        var threshold = p.AutoAcceptThreshold;
        string? skip = trigger != "submit" ? "Manual checks never change the item"
            : threshold <= 0 ? "Auto-accept is off (threshold 0): a reviewer decides"
            : p.ReviewMode != "all" ? "The project has no review step: submitted items are accepted already"
            : verdict != "pass" ? $"Verdict is {verdict}: only passing items can be accepted automatically"
            : confidence is null ? "Rules-only verdict has no model confidence: a reviewer decides"
            : confidence < threshold ? $"Confidence {confidence:P0} is below the threshold {threshold:P0}: sent to a reviewer"
            : null;
        if (skip is null)
        {
            var why = await mediator.Send(new HermesAutoAcceptCommand(p.Id, p.DatasetId, path, ctx.State.Revision, ctx.ActorId), ct);
            if (why is null) { action = "auto_accepted"; steps.Add(new("Auto-accept", "action", "pass", $"Accepted automatically: confidence {confidence:P0} ≥ threshold {threshold:P0}. A Lead or Reviewer can undo it.", 0)); }
            else steps.Add(new("Auto-accept", "action", "skipped", "Not applied: " + why, 0));
        }
        else steps.Add(new("Auto-accept", "action", "skipped", skip, 0));

        var summary = Summary(verdict, steps, confidence, action);
        await mediator.Send(new RecordHermesCheckCommand(id, p.WorkspaceId, p.Id, path, ctx.State.Revision, trigger, verdict, confidence, decidedBy, summary,
            StepsJson(steps), suggestion, runId, action, ctx.ActorId), CancellationToken.None);
        return await mediator.Query(new GetHermesCheckQuery(ws, projectId, id), CancellationToken.None);
    }

    private static string Summary(string verdict, List<Step> steps, double? confidence, string action)
    {
        var first = steps.FirstOrDefault(s => s.Outcome == (verdict == "error" ? "error" : verdict) && s.Kind != "action");
        var head = verdict switch
        {
            "pass" => confidence is { } c ? $"Pass — model {c:P0} sure the labels are correct" : "Pass — rules only",
            "error" => "Check failed to run",
            _ => (verdict == "fail" ? "Fail — " : "Flag — ") + (first?.Detail ?? ""),
        };
        return action == "auto_accepted" ? head + " · accepted automatically" : head;
    }

    private static string StepsJson(List<Step> steps)
    {
        var a = new JsonArray();
        foreach (var s in steps)
            a.Add((JsonNode)new JsonObject { ["name"] = s.Name, ["kind"] = s.Kind, ["outcome"] = s.Outcome, ["detail"] = s.Detail, ["ms"] = s.Ms });
        return a.ToJsonString();
    }

    private static List<Cls> ParseClasses(string json)
    {
        try
        {
            return [.. (JsonNode.Parse(json) as JsonArray ?? []).Select(c => new Cls(c?["id"]?.GetValue<int>() ?? -1, c?["name"]?.ToString() ?? "")).Where(c => c.Id >= 0)];
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return []; }
    }

    // ───────── rules ─────────

    private static readonly string[] Classless = ["relation", "text_pair", "preference", "value", "transcript"];

    private static void RunRules(List<AnnotationRow> anns, List<Cls> classes, List<Step> steps)
    {
        var w = Stopwatch.StartNew();
        steps.Add(anns.Count == 0
            ? new("Item has labels", "rule", "fail", "No labels on this item", 0)
            : new("Item has labels", "rule", "pass", $"{anns.Count} label(s)", 0));

        var ids = classes.Select(c => c.Id).ToHashSet();
        var bad = anns.Select((a, i) => (a, i)).Where(x => !Classless.Contains(x.a.Kind) && !ids.Contains(x.a.ClassId)).Select(x => $"#{x.i + 1}").ToList();
        steps.Add(bad.Count > 0
            ? new("Classes exist in the schema", "rule", "fail", $"Label(s) {string.Join(", ", bad)} use a class that is not in the schema", 0)
            : new("Classes exist in the schema", "rule", anns.Count == 0 ? "skipped" : "pass", anns.Count == 0 ? "Nothing to check" : "All classes are valid", 0));

        var issues = new List<string>();
        var boxes = new List<(int I, int Cls, double X, double Y, double W, double H)>();
        var spans = new List<(int I, int Cls, double S, double E)>();
        var segs = new List<(int I, int Cls, double S, double E)>();
        var shaped = 0;
        for (var i = 0; i < anns.Count; i++)
        {
            var a = anns[i];
            JsonNode? g;
            try { g = JsonNode.Parse(string.IsNullOrEmpty(a.Geometry) ? "{}" : a.Geometry); } catch (JsonException) { continue; }
            double N(string k) => g?[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : double.NaN;
            switch (a.Kind)
            {
                case "bbox":
                    shaped++;
                    var (bx, by, bw, bh) = (N("x"), N("y"), N("w"), N("h"));
                    if (bw * bh < 0.0001) issues.Add($"box #{i + 1} is tiny ({bw:P1} × {bh:P1} of the image)");
                    else if (bw * bh > 0.97) issues.Add($"box #{i + 1} covers almost the whole image");
                    boxes.Add((i, a.ClassId, bx, by, bw, bh));
                    break;
                case "polygon":
                    shaped++;
                    if (g?["points"] is JsonArray pts && pts.Count >= 3)
                    {
                        var area = 0.0;
                        for (var k = 0; k < pts.Count; k++)
                        {
                            var p1 = pts[k]; var p2 = pts[(k + 1) % pts.Count];
                            area += (p1![0]!.GetValue<double>() * p2![1]!.GetValue<double>()) - (p2[0]!.GetValue<double>() * p1[1]!.GetValue<double>());
                        }
                        if (Math.Abs(area) / 2 < 0.0001) issues.Add($"polygon #{i + 1} is tiny or flat");
                    }
                    break;
                case "span":
                    shaped++;
                    if (string.IsNullOrWhiteSpace(g?["text"]?.ToString())) issues.Add($"span #{i + 1} contains only spaces");
                    spans.Add((i, a.ClassId, N("start"), N("end")));
                    break;
                case "segment":
                    shaped++;
                    if (N("end") - N("start") < 0.05) issues.Add($"segment #{i + 1} is shorter than 0.05 s");
                    segs.Add((i, a.ClassId, N("start"), N("end")));
                    break;
                case "keypoints": case "mask": shaped++; break;
                case "transcript":
                    shaped++;
                    if (string.IsNullOrWhiteSpace(g?["text"]?.ToString() ?? a.Attributes)) issues.Add($"transcript #{i + 1} is empty");
                    break;
            }
        }
        for (var x = 0; x < boxes.Count; x++)
            for (var y = x + 1; y < boxes.Count; y++)
            {
                var (a, b) = (boxes[x], boxes[y]);
                if (a.Cls != b.Cls) continue;
                var iw = Math.Max(0, Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X));
                var ih = Math.Max(0, Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y));
                var inter = iw * ih;
                var iou = inter / (a.W * a.H + b.W * b.H - inter);
                if (iou > 0.9) issues.Add($"boxes #{a.I + 1} and #{b.I + 1} overlap {iou:P0} with the same class (duplicate?)");
            }
        foreach (var (list, what) in new[] { (spans, "spans"), (segs, "segments") })
            for (var x = 0; x < list.Count; x++)
                for (var y = x + 1; y < list.Count; y++)
                {
                    var (a, b) = (list[x], list[y]);
                    if (a.S < b.E && b.S < a.E && a.Cls != b.Cls) issues.Add($"{what} #{a.I + 1} and #{b.I + 1} overlap with different classes");
                    else if (Math.Abs(a.S - b.S) < 1e-6 && Math.Abs(a.E - b.E) < 1e-6) issues.Add($"{what} #{a.I + 1} and #{b.I + 1} are duplicates");
                }
        steps.Add(shaped == 0
            ? new("Shapes are plausible", "rule", "skipped", "No shapes, spans or segments to check", (int)w.ElapsedMilliseconds)
            : issues.Count > 0
                ? new("Shapes are plausible", "rule", "flag", string.Join("; ", issues.Take(6)) + (issues.Count > 6 ? $"; +{issues.Count - 6} more" : ""), (int)w.ElapsedMilliseconds)
                : new("Shapes are plausible", "rule", "pass", $"{shaped} shape(s) look fine", (int)w.ElapsedMilliseconds));
    }

    // ───────── model input ─────────

    private static string BuildState(ProjectRow p, List<Cls> classes, string text, List<AnnotationRow> anns)
    {
        var sb = new StringBuilder();
        sb.Append("Labeling task: project \"").Append(p.Name).Append("\".\n");
        if (!string.IsNullOrWhiteSpace(p.GuidelineMd)) sb.Append("Guideline:\n").Append(AiProviders.Clip(p.GuidelineMd.Trim(), 1500)).Append('\n');
        sb.Append("Classes: ").Append(string.Join("; ", classes.Select(c => $"{c.Id} = {c.Name}"))).Append('\n');
        sb.Append("Item text:\n\"\"\"\n").Append(text).Append("\n\"\"\"\n");
        sb.Append("Labels given by the annotator:\n");
        foreach (var a in anns)
        {
            var name = classes.FirstOrDefault(c => c.Id == a.ClassId)?.Name ?? a.ClassId.ToString(CultureInfo.InvariantCulture);
            string? quote = null;
            try { quote = JsonNode.Parse(a.Geometry)?["text"]?.ToString(); } catch (JsonException) { }
            sb.Append(a.Kind switch
            {
                "tag" => $"- whole item: {name}\n",
                "span" => $"- span \"{AiProviders.Clip(quote ?? "", 200)}\": {name}\n",
                _ => $"- {a.Kind}: {name}\n",
            });
        }
        return sb.ToString();
    }
}
