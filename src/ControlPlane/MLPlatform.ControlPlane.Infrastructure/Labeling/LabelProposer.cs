using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Ai;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Infrastructure;

/*
 * AI label proposals (Hermes phase 3). One call = one suggestion set stored for the item, pending until a person applies it.
 * Sources: an AI model from the AI page (labeler/vision role; LLM output parsed from JSON) or one of the workspace's deployments
 * (standard ModelServer protocol: detect/classify/ner/asr; candidate labels are sent for zero-shot models). Model labels map to
 * schema classes by the project's mapping or by name; what does not map is listed, never silently dropped. External APIs need
 * the project opt-in and a confirmation for the call. Every call is an AI run anyone in the workspace can open.
 */
public sealed class LabelProposer(AiRuntime ai, ItemContent content, ModelServerClients servers, IClock clock, ILogger<LabelProposer> log)
{
    private sealed record Cls(int Id, string Name);
    private sealed record Proposed(string Kind, int ClassId, JsonObject Geometry, double? Confidence, string Label);

    /// <summary>Describes a source for the settings page and validation. Returns null when it does not exist.</summary>
    public sealed record SourceInfo(string Kind, Guid Id, string Label, bool External, string State, string Reason, string[] Media, string Detail,
        AiModelRow? Model = null, AiConnectionRow? Connection = null, DeploymentRow? Deployment = null);

    public async Task<List<SourceInfo>> ListSourcesAsync(IMediator mediator, Guid ws, CancellationToken ct)
    {
        var res = new List<SourceInfo>();
        var cat = await ai.CatalogAsync(ct);
        var conns = cat.Connections.ToDictionary(c => c.Id);
        foreach (var m in cat.Models.Where(m => m.Roles.Contains("labeler") || m.Roles.Contains("vision")))
        {
            var c = conns[m.ConnectionId];
            var media = new List<string>();
            if (m.Roles.Contains("labeler") && m.Inputs.Contains("text")) media.Add("text");
            if (m.Roles.Contains("vision") && m.Inputs.Contains("image")) media.Add("image");
            var (state, reason) = !m.IsActive ? ("unavailable", "Model is turned off")
                : !c.IsActive ? ("unavailable", $"Connection {c.Name} is turned off")
                : AiProviders.KeyRequired(c.ProviderType) && c.SecretEnc is null ? ("unavailable", $"Connection {c.Name} has no API key")
                : m.Health == "failed" ? ("degraded", "Failed: " + m.HealthReason)
                : m.Health == "unknown" ? ("degraded", "Not tested yet")
                : ("ready", m.Health == "degraded" ? "Recent failure: " + m.HealthReason : "Healthy");
            res.Add(new("model", m.Id, AiRuntime.Label(m, c), c.IsExternal, state, reason, [.. media],
                $"AI model · roles {string.Join(", ", m.Roles)}", m, c));
        }
        foreach (var d in await mediator.Query(new ListAssistDeploymentsQuery(ws), ct))
        {
            var media = d.Task switch
            {
                "detect" or "segment" or "classify" or "obb" or "zero-shot-detect" or "zero-shot-image-classify" => new[] { "image" },
                "text-classify" or "ner" or "zero-shot-classify" => ["text"],
                "asr" or "audio-classify" => ["audio"],
                _ => [],
            };
            var (state, reason) = d.State == "healthy" && d.Endpoint is not null ? ("ready", "Healthy") : ("unavailable", $"Deployment is {d.State}" + (d.StateReason is { } r ? $": {r}" : ""));
            res.Add(new("deployment", d.Id, $"{d.Slug} ({d.ModelName} v{d.ModelVersion})", false, state, reason, media, $"Deployment · task {d.Task}", Deployment: d));
        }
        return res;
    }

    public async Task<SuggestionSetRow> ProposeAsync(IMediator mediator, HermesContext ctx, ProjectAssistSettings settings, string kind, Guid sourceId,
        bool externalConfirmed, string trigger, Guid? jobId, CancellationToken ct)
    {
        var p = ctx.Project;
        var path = ctx.State.Path;
        var sources = await ListSourcesAsync(mediator, p.WorkspaceId, ct);
        var src = sources.FirstOrDefault(s => s.Kind == kind && s.Id == sourceId) ?? throw new NotFoundException("LABEL_SOURCE_NOT_FOUND", "Proposal source not found");
        if (!settings.IsEnabled(kind, sourceId, src.External)) throw new PreconditionException("LABEL_SOURCE_NOT_ENABLED", "This source is not enabled in the project's AI assist settings");
        if (src.External && !settings.AllowExternal) throw new PreconditionException("LABEL_EXTERNAL_OFF", "External APIs are turned off for this project");
        if (src.External && !externalConfirmed) throw new PreconditionException("LABEL_EXTERNAL_CONFIRM", "Sending an item to an external API needs an explicit confirmation");

        var classes = ParseClasses(ctx.Classes);
        var media = HermesLabelChecker.Media(path);
        var w = Stopwatch.StartNew();
        Guid? runId = null;
        var items = new List<Proposed>();
        var unmapped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string state = "proposed", detail;
        var dropped = 0;
        try
        {
            if (!src.Media.Contains(media))
                throw new ProposalException($"{src.Label} cannot propose labels for {media} items (it handles {(src.Media.Length == 0 ? "nothing usable for labeling" : string.Join(", ", src.Media))})");
            if (src.State == "unavailable") throw new ProposalException($"{src.Label} is not available: {src.Reason}");

            int ClassOf(string label)
            {
                if (settings.ClassMapping.TryGetValue(label.Trim(), out var mapped)) return mapped;
                return classes.FirstOrDefault(c => string.Equals(c.Name, label.Trim(), StringComparison.OrdinalIgnoreCase))?.Id ?? -1;
            }
            void Add(string k, string label, JsonObject g, double? conf)
            {
                if (conf is { } cv && cv < settings.MinConfidence) { dropped++; return; }
                var id = ClassOf(label);
                if (id < 0 && k != "transcript") { unmapped[label] = unmapped.GetValueOrDefault(label) + 1; return; }
                items.Add(new(k, Math.Max(0, id), g, conf, label));
            }

            if (kind == "model")
                runId = await FromModelAsync(ctx, src, classes, media, trigger, Add, ct);
            else
                runId = await FromDeploymentAsync(mediator, ctx, src, classes, media, trigger, Add, ct);

            detail = items.Count == 0 && unmapped.Count == 0
                ? (dropped > 0 ? $"No label reached the minimum confidence {settings.MinConfidence:P0} ({dropped} below)" : "The model proposed no labels")
                : $"{items.Count} label(s) proposed" + (dropped > 0 ? $"; {dropped} below the minimum confidence {settings.MinConfidence:P0}" : "")
                  + (unmapped.Count > 0 ? $"; {unmapped.Values.Sum()} with labels that match no schema class" : "");
        }
        catch (ProposalException e) { state = "failed"; detail = e.Message; runId = e.RunId ?? runId; }
        catch (Exception e) when (e is not (OperationCanceledException or AppException))
        {
            log.LogWarning(e, "Proposal for {Path} failed", path);
            state = "failed"; detail = "The proposal stopped unexpectedly: " + e.Message;
        }

        var id = Ids.New();
        var arr = new JsonArray();
        foreach (var x in items)
        {
            var o = new JsonObject { ["kind"] = x.Kind, ["class_id"] = x.ClassId, ["geometry"] = x.Geometry, ["label"] = x.Label };
            if (x.Confidence is { } c) o["confidence"] = Math.Round(c, 4);
            arr.Add((JsonNode)o);
        }
        var um = new JsonArray();
        foreach (var (k, v) in unmapped) um.Add((JsonNode)new JsonObject { ["label"] = k, ["count"] = v });
        await mediator.Send(new RecordSuggestionSetCommand(id, p.WorkspaceId, p.Id, path, ctx.State.Revision, kind, sourceId, src.Label, src.External, state, detail,
            arr.ToJsonString(), um.ToJsonString(), runId, (int)w.ElapsedMilliseconds, jobId, ctx.ActorId), CancellationToken.None);
        return jobId is null
            ? await mediator.Query(new GetSuggestionSetQuery(p.WorkspaceId, p.Id, id), CancellationToken.None)
            : new SuggestionSetRow(id, p.Id, path, ctx.State.Revision, kind, sourceId, src.Label, src.External, state, detail, arr.ToJsonString(), um.ToJsonString(),
                runId, (int)w.ElapsedMilliseconds, jobId, "pending", null, clock.UtcNow, null);
    }

    private sealed class ProposalException(string message, Guid? runId = null) : Exception(message) { public Guid? RunId { get; } = runId; }

    // ───────── AI model (LLM) ─────────

    private async Task<Guid?> FromModelAsync(HermesContext ctx, SourceInfo src, List<Cls> classes, string media, string trigger,
        Action<string, string, JsonObject, double?> add, CancellationToken ct)
    {
        var p = ctx.Project;
        var names = string.Join(", ", classes.Select(c => c.Name));
        var guideline = string.IsNullOrWhiteSpace(p.GuidelineMd) ? "(none)" : AiProviders.Clip(p.GuidelineMd.Trim(), 1500);
        var task = ctx.DatasetTask;
        AiMessage user;
        string role;
        if (media == "text")
        {
            var (text, note) = await content.ReadTextAsync(ctx.Source, 12_000, ct);
            if (text is null) throw new ProposalException("Could not read the item text: " + note);
            var shape = task switch
            {
                "ner" => "Use only \"spans\" (entity mentions); leave \"tags\" empty.",
                "text-classify" => "Give exactly one tag for the whole item; leave \"spans\" empty.",
                _ => "Use \"tags\" for labels of the whole item and \"spans\" only for entity mentions; leave a list empty when it does not apply.",
            };
            user = new AiMessage("user",
                $"Task: {task}.\nGuideline:\n{guideline}\nClasses (use these names exactly): {names}\nItem text:\n\"\"\"\n{text}\n\"\"\"\n" +
                "Return JSON only: {\"tags\":[{\"class\":\"<class name>\",\"confidence\":0.0}],\"spans\":[{\"text\":\"<exact substring of the item text>\",\"class\":\"<class name>\",\"confidence\":0.0}]}\n" + shape);
            role = "labeler";
        }
        else
        {
            var (bytes, err) = await content.ReadAsync(ctx.Source, 8 * 1024 * 1024, ct);
            if (bytes is null) throw new ProposalException("Could not read the image: " + err);
            user = new AiMessage("user",
                $"Task: {task}.\nGuideline:\n{guideline}\nClasses (use these names exactly): {names}\n" +
                "Which classes describe this image? Return JSON only: {\"tags\":[{\"class\":\"<class name>\",\"confidence\":0.0}]}. " +
                "Do not return boxes: language models place boxes poorly — boxes come from a detection deployment.",
                [new AiImage(ItemContent.Mime(ctx.State.Path), bytes)]);
            role = "vision";
        }
        var r = await ai.ChatAsync(new AiCallSpec("chat", role, "Label proposal", $"{p.Name} · {ctx.State.Path}", trigger, p.WorkspaceId, ctx.ActorId,
            NeedInputs: media == "image" ? ["image"] : ["text"], PinnedModel: src.Id, DeadlineMs: 60_000, MaxTokens: 1200, UpdateHealth: true),
            [new AiMessage("system", "You label data for a machine learning dataset. Answer with one JSON object only, no prose, no code fences."), user], ct);
        if (!r.Ok || r.Text is null) throw new ProposalException($"No answer ({r.ErrorCode}): {r.Error}", r.RunId);

        JsonNode? json;
        try
        {
            var t = r.Text;
            var a = t.IndexOf('{'); var b = t.LastIndexOf('}');
            json = a >= 0 && b > a ? JsonNode.Parse(t[a..(b + 1)]) : null;
        }
        catch (JsonException) { json = null; }
        if (json is null) throw new ProposalException("The model did not return valid JSON: " + AiProviders.Clip(r.Text.ReplaceLineEndings(" "), 200), r.RunId);

        static double? Conf(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? Math.Clamp(d, 0, 1) : null;
        foreach (var tg in json["tags"] as JsonArray ?? [])
            if (tg?["class"]?.ToString() is { Length: > 0 } cls) add("tag", cls, new JsonObject(), Conf(tg["confidence"]));
        if (media == "text")
        {
            var (text, _) = await content.ReadTextAsync(ctx.Source, 12_000, ct);
            var from = 0;
            foreach (var sp in json["spans"] as JsonArray ?? [])
            {
                var quote = sp?["text"]?.ToString();
                var cls = sp?["class"]?.ToString();
                if (string.IsNullOrEmpty(quote) || string.IsNullOrEmpty(cls) || text is null) continue;
                var at = text.IndexOf(quote, from, StringComparison.Ordinal);
                if (at < 0) at = text.IndexOf(quote, StringComparison.Ordinal);
                if (at < 0) continue;   // the model quoted text that is not in the item — ignored (the AI run shows it)
                add("span", cls, new JsonObject { ["start"] = at, ["end"] = at + quote.Length, ["text"] = quote }, Conf(sp?["confidence"]));
                from = at + quote.Length;
            }
        }
        return r.RunId;
    }

    // ───────── deployment (ModelServer) ─────────

    private async Task<Guid?> FromDeploymentAsync(IMediator mediator, HermesContext ctx, SourceInfo src, List<Cls> classes, string media, string trigger,
        Action<string, string, JsonObject, double?> add, CancellationToken ct)
    {
        var d = src.Deployment!;
        var p = ctx.Project;
        var candidates = classes.Select(c => c.Name).ToList();
        var input = new TaskInput();
        var inputSummary = new JsonObject { ["deployment"] = d.Slug, ["task"] = d.Task, ["path"] = ctx.State.Path, ["candidate_labels"] = new JsonArray([.. candidates.Select(c => (JsonNode)JsonValue.Create(c))]) };
        switch (media)
        {
            case "image":
            {
                var (bytes, err) = await content.ReadAsync(ctx.Source, 20 * 1024 * 1024, ct);
                if (bytes is null) throw new ProposalException("Could not read the image: " + err);
                input.Image = new ImageInput { Data = Google.Protobuf.ByteString.CopyFrom(bytes), Conf = 0.05f, Iou = 0.5f };
                input.Image.Labels.AddRange(candidates);
                inputSummary["image_bytes"] = bytes.Length;
                break;
            }
            case "text":
            {
                var (text, note) = await content.ReadTextAsync(ctx.Source, 100_000, ct);
                if (text is null) throw new ProposalException("Could not read the item text: " + note);
                input.Text = new TextInput { Text = text };
                input.Text.Labels.AddRange(candidates);
                inputSummary["text_chars"] = text.Length;
                break;
            }
            case "audio":
            {
                var (bytes, err) = await content.ReadAsync(ctx.Source, 50 * 1024 * 1024, ct);
                if (bytes is null) throw new ProposalException("Could not read the audio: " + err);
                input.Audio = new AudioInput { Data = Google.Protobuf.ByteString.CopyFrom(bytes), Format = Path.GetExtension(ctx.State.Path).TrimStart('.') };
                inputSummary["audio_bytes"] = bytes.Length;
                break;
            }
            default: throw new ProposalException($"Deployments cannot propose labels for {media} items yet");
        }

        // Recorded like every AI call: the deployment, what was sent (bytes are not stored, only their size) and what came back.
        var runId = Ids.New();
        var selection = new JsonObject { ["role"] = "deployment", ["note"] = "Deployment chosen in the project's AI assist settings", ["candidates"] = new JsonArray() };
        await mediator.Send(new StartAiRunCommand(runId, p.WorkspaceId, "predict", "Label proposal", $"{p.Name} · {ctx.State.Path}", trigger, ctx.ActorId,
            null, src.Label, selection.ToJsonString(), inputSummary.ToJsonString()), CancellationToken.None);
        var w = Stopwatch.StartNew();
        var attempts = new JsonArray();
        PredictResponse res;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            var req = new PredictRequest();
            req.Inputs.Add(input);
            res = await servers.For(d.Endpoint!).PredictAsync(req, cancellationToken: cts.Token);
        }
        catch (Exception e) when (e is Grpc.Core.RpcException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            var msg = e is Grpc.Core.RpcException re ? $"{re.StatusCode}: {re.Status.Detail}" : "No answer within 60 s";
            attempts.Add((JsonNode)new JsonObject { ["number"] = 1, ["label"] = src.Label, ["status"] = 0, ["latency_ms"] = (int)w.ElapsedMilliseconds, ["outcome"] = "unreachable", ["detail"] = msg });
            await mediator.Send(new FinishAiRunCommand(runId, "failed", null, null, src.Label, "{}", "none", null, (int)w.ElapsedMilliseconds, 0, 0, null,
                attempts.ToJsonString(), "SERVE_REPLICA_ERROR", msg, false, null), CancellationToken.None);
            throw new ProposalException($"The deployment did not answer: {msg}", runId);
        }
        attempts.Add((JsonNode)new JsonObject { ["number"] = 1, ["label"] = src.Label, ["status"] = 200, ["latency_ms"] = (int)w.ElapsedMilliseconds, ["outcome"] = "ok" });
        var output = new JsonObject();
        var summary = new List<string>();
        foreach (var o in res.Outputs)
        {
            switch (o.KindCase)
            {
                case TaskOutput.KindOneofCase.Detect:
                {
                    var det = o.Detect;
                    output["detect"] = new JsonObject { ["width"] = det.Width, ["height"] = det.Height, ["objects"] = det.Objects.Count };
                    if (det.Width <= 0 || det.Height <= 0) { summary.Add("detections without image size (cannot place boxes)"); break; }
                    foreach (var ob in det.Objects)
                    {
                        var name = string.IsNullOrEmpty(ob.ClassName) ? ob.ClassId.ToString(CultureInfo.InvariantCulture) : ob.ClassName;
                        if (ob.Polygon.Count >= 6)
                        {
                            var pts = new JsonArray();
                            for (var i = 0; i + 1 < ob.Polygon.Count; i += 2)
                                pts.Add((JsonNode)new JsonArray(R(ob.Polygon[i] / det.Width), R(ob.Polygon[i + 1] / det.Height)));
                            add("polygon", name, new JsonObject { ["points"] = pts }, ob.Score);
                        }
                        else
                        {
                            double x = Math.Clamp(ob.X1 / det.Width, 0, 1), y = Math.Clamp(ob.Y1 / det.Height, 0, 1);
                            double x2 = Math.Clamp(ob.X2 / det.Width, 0, 1), y2 = Math.Clamp(ob.Y2 / det.Height, 0, 1);
                            if (x2 <= x || y2 <= y) continue;
                            add("bbox", name, new JsonObject { ["x"] = R(x), ["y"] = R(y), ["w"] = R(x2 - x), ["h"] = R(y2 - y) }, ob.Score);
                        }
                    }
                    summary.Add($"{det.Objects.Count} object(s)");
                    break;
                }
                case TaskOutput.KindOneofCase.Classify:
                {
                    var top = o.Classify.Labels.OrderByDescending(l => l.Score).Take(3).ToList();
                    output["classify"] = new JsonArray([.. top.Select(l => (JsonNode)new JsonObject { ["label"] = l.Label, ["score"] = l.Score })]);
                    if (top.Count > 0) add("tag", top[0].Label, new JsonObject(), top[0].Score);
                    summary.Add(top.Count > 0 ? $"top class {top[0].Label} ({top[0].Score:P0})" : "no class");
                    break;
                }
                case TaskOutput.KindOneofCase.Ner:
                    foreach (var e in o.Ner.Entities)
                        add("span", e.Label, new JsonObject { ["start"] = e.Start, ["end"] = e.End, ["text"] = e.Text }, e.Score);
                    output["ner"] = o.Ner.Entities.Count;
                    summary.Add($"{o.Ner.Entities.Count} entit(ies)");
                    break;
                case TaskOutput.KindOneofCase.Asr:
                    add("transcript", "transcript", new JsonObject { ["text"] = o.Asr.Text }, null);
                    output["asr"] = AiProviders.Clip(o.Asr.Text, 2000);
                    summary.Add("transcript");
                    break;
                default:
                    summary.Add($"{o.KindCase} output cannot become labels");
                    break;
            }
        }
        output["summary"] = summary.Count == 0 ? "No output" : string.Join("; ", summary);
        await mediator.Send(new FinishAiRunCommand(runId, "succeeded", null, null, src.Label, output.ToJsonString(), "model", null, (int)w.ElapsedMilliseconds,
            0, 0, null, attempts.ToJsonString(), null, null, false, null), CancellationToken.None);
        return runId;
    }

    private static double R(double v) => Math.Round(v, 4);

    private static List<Cls> ParseClasses(string json)
    {
        try { return [.. (JsonNode.Parse(json) as JsonArray ?? []).Select(c => new Cls(c?["id"]?.GetValue<int>() ?? -1, c?["name"]?.ToString() ?? "")).Where(c => c.Id >= 0)]; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return []; }
    }
}
