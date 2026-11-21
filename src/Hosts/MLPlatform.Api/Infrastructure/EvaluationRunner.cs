using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Google.Protobuf;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Infrastructure;

/// <summary>
/// Runs model evaluations (Models → Evaluate): ensures a replica of the model version (reused or a test replica), then for each
/// sample downloads the image + ground truth, calls the replica and stores the per-sample result; at the end the metrics are
/// computed from the stored samples. Resumable: samples already stored are skipped after an api restart.
/// </summary>
public sealed class EvaluationRunner(IServiceScopeFactory scopes, ModelServerClients clients, IHttpClientFactory http, SystemHealth health,
    ILogger<EvaluationRunner> log) : BackgroundService
{
    private const int Parallel = 2;
    private readonly ConcurrentDictionary<string, Dictionary<string, List<EvalBox>>> _coco = new();

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stop))
        {
            health.Beat("evaluation", 5);
            try
            {
                IReadOnlyList<EvaluationRow> active;
                await using (var s0 = scopes.CreateAsyncScope())
                    active = await s0.ServiceProvider.GetRequiredService<IMediator>().Query(new ListActiveEvaluationsQuery(), stop);
                foreach (var e in active) await StepAsync(e, stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Evaluation loop failed");
            }
        }
    }

    private async Task StepAsync(EvaluationRow e, CancellationToken stop)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUser>().UserId = e.CreatedBy;   // the engine acts as the requester
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        try
        {
            var dep = await m.Send(new StartTestDeploymentCommand(e.WorkspaceId, e.ModelVersionId, -1, null), stop);
            if (dep.State is "failed" or "stopped")
            {
                await m.Send(new SetEvaluationStateCommand(e.Id, "failed", $"Replica {dep.Slug} {dep.State}: {dep.StateReason}", dep.Id), stop);
                return;
            }
            if (dep.State != "healthy" || dep.Endpoint is null)
            {
                var why = $"Waiting for replica {dep.Slug} ({dep.State})";
                if (e.State != "preparing" || e.StateReason != why) await m.Send(new SetEvaluationStateCommand(e.Id, "preparing", why, dep.Id), stop);
                return;
            }
            var plan = await m.Query(new PlanEvaluationQuery(e.Id), stop);
            await m.Send(new SetEvaluationStateCommand(e.Id, "running", null, dep.Id, plan.Samples.Count), stop);
            log.LogInformation("Evaluation {Id}: {N} samples on {Slug}", e.Id, plan.Samples.Count, dep.Slug);
            await RunSamplesAsync(e, plan, dep, stop);
            await using var s2 = scopes.CreateAsyncScope();
            var m2 = s2.ServiceProvider.GetRequiredService<IMediator>();
            var now = await m2.Query(new ListActiveEvaluationsQuery(), stop);
            if (now.All(x => x.Id != e.Id)) return;   // cancelled meanwhile
            await m2.Send(new CompleteEvaluationCommand(e.Id), stop);
            log.LogInformation("Evaluation {Id} finished", e.Id);
        }
        catch (AppException ex)
        {
            await using var s3 = scopes.CreateAsyncScope();
            await s3.ServiceProvider.GetRequiredService<IMediator>().Send(new SetEvaluationStateCommand(e.Id, "failed", ex.Message, null), stop);
        }
    }

    private async Task RunSamplesAsync(EvaluationRow e, EvaluationPlan plan, DeploymentRow dep, CancellationToken stop)
    {
        var todo = new ConcurrentQueue<(int Idx, string Image, string ImageUrl, string? Label, string? LabelUrl, string? ClassName)>(
            plan.Samples.Where(s => !plan.Done.Contains(s.Idx)));
        Dictionary<string, List<EvalBox>>? coco = null;
        if (plan.CocoUrl is { } cu && !_coco.TryGetValue(e.Id.ToString(), out coco))
            _coco[e.Id.ToString()] = coco = EvalMath.ParseCoco(System.Text.Encoding.UTF8.GetString(await DownloadAsync(cu, stop)));
        var classes = plan.Classes.Select(EvalMath.Norm).ToList();
        var cancelled = false;
        var processed = 0;

        async Task Worker()
        {
            while (!cancelled && todo.TryDequeue(out var s))
            {
                var row = await EvaluateOneAsync(plan.Task, s, classes, coco, dep, e.ConfThreshold, stop);
                await using var scope = scopes.CreateAsyncScope();
                var m = scope.ServiceProvider.GetRequiredService<IMediator>();
                await m.Send(row with { EvaluationId = e.Id }, stop);
                await m.Send(new TouchDeploymentCommand(dep.Id), stop);
                if (Interlocked.Increment(ref processed) % 10 == 0 && (await m.Query(new ListActiveEvaluationsQuery(), stop)).All(x => x.Id != e.Id))
                    cancelled = true;
            }
        }
        await Task.WhenAll(Enumerable.Range(0, Parallel).Select(_ => Worker()));
        _coco.TryRemove(e.Id.ToString(), out _);
    }

    private async Task<RecordEvalSampleCommand> EvaluateOneAsync(string task, (int Idx, string Image, string ImageUrl, string? Label, string? LabelUrl, string? ClassName) s,
        List<string> classes, Dictionary<string, List<EvalBox>>? coco, DeploymentRow dep, double conf, CancellationToken stop)
    {
        var fail = new RecordEvalSampleCommand(Guid.Empty, s.Idx, s.Image, 0, 0, "[]", "[]", 0, 0, 0, false, null, 0);
        byte[] bytes;
        try { bytes = await DownloadAsync(s.ImageUrl, stop); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return fail with { Error = "Image download failed: " + ex.Message }; }
        var detect = EvalMath.DetectTasks.Contains(task);
        var input = new ImageInput { Data = ByteString.CopyFrom(bytes), Conf = detect ? 0.01f : 0.0f };
        if (task.StartsWith("zero-shot", StringComparison.Ordinal)) input.Labels.AddRange(classes);
        var req = new PredictRequest();
        req.Inputs.Add(new TaskInput { Image = input });
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        PredictResponse res;
        try { res = await clients.For(dep.Endpoint!).PredictAsync(req, deadline: TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(2), cancellationToken: stop); }
        catch (Grpc.Core.RpcException ex) { return fail with { Error = "Replica error: " + ex.Status.Detail }; }
        var latency = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var output = res.Outputs.FirstOrDefault();
        string Name(int id, string name) => name.Length > 0 ? EvalMath.Norm(name) : id >= 0 && id < classes.Count ? classes[id] : id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (detect)
        {
            var size = output?.Detect is { Width: > 0, Height: > 0 } d ? (d.Width, d.Height) : EvalMath.ImageSize(bytes) ?? (0, 0);
            List<EvalBox> gt;
            if (coco is not null) gt = coco.GetValueOrDefault(Path.GetFileName(s.Image)) ?? [];
            else if (s.LabelUrl is null) gt = [];   // no label file → negative image
            else
            {
                try { gt = EvalMath.ParseYolo(System.Text.Encoding.UTF8.GetString(await DownloadAsync(s.LabelUrl, stop)), classes, size.Item1, size.Item2); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return fail with { Error = "Label download failed: " + ex.Message }; }
            }
            var pred = (output?.Detect?.Objects ?? []).OrderByDescending(o => o.Score).Take(100)
                .Select(o => new EvalBox(Name(o.ClassId, o.ClassName), o.X1, o.Y1, o.X2, o.Y2, o.Score)).ToList();
            var (tp, fp, fn) = EvalMath.CountAt(gt, pred, conf);
            return new RecordEvalSampleCommand(Guid.Empty, s.Idx, s.Image, size.Item1, size.Item2, BoxesJson(gt, false), BoxesJson(pred, true),
                tp, fp, fn, fp == 0 && fn == 0, null, latency);
        }

        var labels = output?.Classify?.Labels.OrderByDescending(l => l.Score).ToList() ?? [];
        var top = labels.FirstOrDefault();
        var truth = EvalMath.Norm(s.ClassName ?? "");
        var predicted = top is null ? "" : EvalMath.Norm(top.Label);
        var predJson = new JsonObject
        {
            ["c"] = predicted, ["s"] = Math.Round(top?.Score ?? 0, 4),
            ["top"] = new JsonArray(labels.Take(5).Select(l => (JsonNode)new JsonObject { ["c"] = EvalMath.Norm(l.Label), ["s"] = Math.Round(l.Score, 4) }).ToArray()),
        };
        var ok = predicted == truth;
        return new RecordEvalSampleCommand(Guid.Empty, s.Idx, s.Image, 0, 0, new JsonObject { ["c"] = truth }.ToJsonString(), predJson.ToJsonString(),
            ok ? 1 : 0, ok ? 0 : 1, ok ? 0 : 1, ok, null, latency);
    }

    private static string BoxesJson(IEnumerable<EvalBox> boxes, bool withScore) =>
        new JsonArray(boxes.Select(b =>
        {
            var o = new JsonObject
            {
                ["c"] = b.Cls,
                ["b"] = new JsonArray(Math.Round(b.X1, 1), Math.Round(b.Y1, 1), Math.Round(b.X2, 1), Math.Round(b.Y2, 1)),
            };
            if (withScore) o["s"] = Math.Round(b.Score, 4);
            return (JsonNode)o;
        }).ToArray()).ToJsonString();

    /// <summary>GET with a pause on rate limits (Hugging Face answers 429 after a burst of anonymous downloads).</summary>
    private async Task<byte[]> DownloadAsync(string url, CancellationToken ct)
    {
        var client = http.CreateClient("eval");
        for (var attempt = 0; ; attempt++)
        {
            using var res = await client.GetAsync(url, ct);
            if ((int)res.StatusCode is 429 or 503 && attempt < 4)
            {
                var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5 * (attempt + 1));
                await Task.Delay(wait > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : wait, ct);
                continue;
            }
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsByteArrayAsync(ct);
        }
    }
}
