using MLPlatform.Api.Middleware;
using MLPlatform.ControlPlane.Infrastructure;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using MLPlatform.Contracts.V1;
using ServiceDescriptor = Google.Protobuf.Reflection.ServiceDescriptor;

namespace MLPlatform.Api.Http.Mcp;

/// <summary>
/// MCP server (Model Context Protocol, Streamable HTTP transport, JSON responses) at <c>POST /mcp</c>: external AI agents (Claude, Cursor,
/// custom agents) operate the platform with a user API key (<c>Authorization: Bearer …</c>). Every tool is one platform RPC called over the
/// local gRPC port with the caller's token, so permissions, validation and business rules are exactly the platform's own; input schemas are
/// generated from the proto messages. No delete operations are exposed.
/// </summary>
public sealed class McpServer(IConfiguration cfg, ILogger<McpServer> log)
{
    private const string Protocol = "2025-06-18";
    private static readonly string[] Supported = ["2025-06-18", "2025-03-26", "2024-11-05"];
    private readonly GrpcChannel _channel = GrpcChannel.ForAddress(cfg["Mcp:GrpcAddress"] ?? "http://127.0.0.1:5080");

    /// <summary>One tool = one RPC. ReadOnly tools never change anything.</summary>
    private sealed record Tool(string Name, ServiceDescriptor Service, string Method, string Description, bool ReadOnly);

    private static readonly Tool[] Tools =
    [
        new("list_workspaces", IamService.Descriptor, "ListWorkspaces", "Workspaces the caller belongs to (id, slug, role). Most tools take workspace_id; it defaults to the first workspace.", true),
        new("whoami", IamService.Descriptor, "GetMe", "The signed-in user (the API key's owner).", true),

        new("list_datasets", DataService.Descriptor, "ListDatasets", "Datasets of the workspace (task, file count, versions).", true),
        new("get_dataset", DataService.Descriptor, "GetDataset", "One dataset by id.", true),
        new("list_dataset_versions", DataService.Descriptor, "ListVersions", "Frozen versions of a dataset (file counts, splits).", true),
        new("browse_dataset_files", DataService.Descriptor, "BrowseFiles", "Browse a dataset's files/folders with search and kind filters.", true),

        new("list_labeling_projects", LabelingService.Descriptor, "ListProjects", "Labeling projects with progress.", true),
        new("labeling_project_stats", LabelingService.Descriptor, "GetProjectStats", "Counts per status, members and guideline of one labeling project.", true),
        new("list_label_items", LabelingService.Descriptor, "ListItems", "Items of a labeling project (path, status, label count, thumbnail url); filter by status, search, paging.", true),
        new("get_label_item", LabelingService.Descriptor, "GetItem", "One item: signed image url, classes (ids, names), current labels with geometry (normalized 0..1 boxes x,y,w,h).", true),
        new("ai_propose_labels", LabelingService.Descriptor, "ProposeItem", "Ask an AI source (deployment or model) to propose labels for one item; returns proposed boxes with confidence. Does not save them.", false),
        new("label_ai_sources", LabelingService.Descriptor, "GetAssistSettings", "AI sources usable for proposals in a project and their state.", true),
        new("claim_label_item", LabelingService.Descriptor, "ClaimItem", "Take the edit lock of an item (needed before saving labels).", false),
        new("save_labels", LabelingService.Descriptor, "SaveAnnotations", "Replace an item's labels (claim it first; etag from get/claim). Each label: kind=bbox, class_id, geometry_json={\"x\",\"y\",\"w\",\"h\"} normalized, source=human|model|ai.", false),
        new("submit_label_item", LabelingService.Descriptor, "SubmitItem", "Submit a labeled item (goes to review or accepted).", false),
        new("accept_label_item", LabelingService.Descriptor, "AcceptItem", "Reviewer: accept an item.", false),
        new("return_label_item", LabelingService.Descriptor, "RejectItem", "Reviewer: return an item to its annotator with a comment.", false),

        new("list_training_projects", TrackingService.Descriptor, "ListProjects", "Training projects with their best metric.", true),
        new("list_runs", TrackingService.Descriptor, "ListRuns", "Training runs of a project (state, primary metric, params).", true),
        new("get_run", TrackingService.Descriptor, "GetRun", "One run with params, progress and metrics summary.", true),
        new("get_run_metrics", TrackingService.Descriptor, "GetMetrics", "Metric series of a run (loss, mAP… per step/epoch).", true),
        new("compare_runs", TrackingService.Descriptor, "CompareRuns", "Parameter and metric differences between runs.", true),
        new("list_project_recipes", TrackingService.Descriptor, "ListProjectRecipes", "Recipes adopted by a training project (id needed for the draft/trial/train tools).", true),
        new("get_training_draft", TrackingService.Descriptor, "GetDraft", "The caller's draft (dataset version, variant, params) of a project recipe and whether it may train.", true),
        new("start_trial", TrackingService.Descriptor, "StartTrial", "Quick check of the draft (data, weights, one epoch, resume, package) before a full training.", false),
        new("get_trial", TrackingService.Descriptor, "GetTrial", "State and stages of a trial.", true),
        new("start_training", TrackingService.Descriptor, "StartTraining", "Start a training run.", false),
        new("stop_run", TrackingService.Descriptor, "StopRun", "Stop a running training.", false),

        new("list_models", RegistryService.Descriptor, "ListModels", "Registered models.", true),
        new("list_model_versions", RegistryService.Descriptor, "ListModelVersions", "Versions of a model (stage, metrics, source run).", true),
        new("start_evaluation", RegistryService.Descriptor, "StartEvaluation", "Evaluate a model version on a dataset version (mAP / accuracy).", false),
        new("get_evaluation", RegistryService.Descriptor, "GetEvaluation", "Result of an evaluation.", true),
        new("start_export", RegistryService.Descriptor, "StartExport", "Convert a model version: onnx, onnx-fp16, openvino-fp32/fp16, tensorrt-fp16/fp32 (host_id for TensorRT), or the recipe's own formats.", false),
        new("list_exports", RegistryService.Descriptor, "ListExports", "Conversions of a model version and the formats it supports.", true),
        new("get_export_download", RegistryService.Descriptor, "GetExportDownload", "Signed download link of a finished conversion.", true),

        new("list_deployments", ServingService.Descriptor, "ListDeployments", "Model deployments (state, endpoint).", true),
        new("start_deployment", ServingService.Descriptor, "StartDeployment", "Start a stopped deployment (uses a GPU/CPU on a machine).", false),
        new("stop_deployment", ServingService.Descriptor, "StopDeployment", "Stop a deployment and free its machine.", false),
        new("try_predict", ServingService.Descriptor, "TryPredict", "Run one prediction on a healthy deployment.", false),

        new("list_jobs", SchedulerService.Descriptor, "ListJobs", "Jobs (training, serve, export, optimize…) with state and reason.", true),
        new("get_job", SchedulerService.Descriptor, "GetJobOps", "Progress, attempts, checkpoints and timeline of a job.", true),
        new("get_job_logs", SchedulerService.Descriptor, "GetJobLogs", "Log lines of a job.", true),
        new("cancel_job", SchedulerService.Descriptor, "CancelJob", "Stop a job.", false),

        new("list_machines", FleetService.Descriptor, "ListHosts", "Machines (GPUs, CPU, RAM, state, agent version).", true),
        new("ray_health", OpsService.Descriptor, "GetRayHealth", "Ray cluster health: nodes alive/dead with resource use, placement groups, pending demands.", true),
        new("system_status", OpsService.Descriptor, "GetSystemStatus", "Platform components and background loops.", true),
    ];

    private static readonly Dictionary<string, Tool> ByName = Tools.ToDictionary(t => t.Name);

    public async Task HandleAsync(HttpContext http, Guid? userId)
    {
        if (userId is null)
        {
            http.Response.StatusCode = 401;
            http.Response.Headers.WWWAuthenticate = "Bearer realm=\"mlplatform\", error=\"invalid_token\"";
            await http.Response.WriteAsync("{\"error\":\"Bearer API key required (Settings → API key)\"}");
            return;
        }
        JsonNode? body;
        try { body = await JsonNode.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted); }
        catch (System.Text.Json.JsonException) { await WriteAsync(http, Error(null, -32700, "Parse error")); return; }
        // Bearer API key (agents) or the web session cookie (same-origin tools such as WebMCP in the browser)
        var auth = http.Request.Headers.Authorization.ToString();
        var token = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth[7..] : http.Request.Cookies[AuthMiddleware.Cookie] ?? "";
        if (body is JsonArray batch)
        {
            var outs = new JsonArray();
            foreach (var m in batch) if (await DispatchAsync(m as JsonObject, token, http.RequestAborted) is { } r) outs.Add(r);
            if (outs.Count == 0) { http.Response.StatusCode = 202; return; }
            await WriteAsync(http, outs);
            return;
        }
        var res = await DispatchAsync(body as JsonObject, token, http.RequestAborted);
        if (res is null) { http.Response.StatusCode = 202; return; }   // notification
        await WriteAsync(http, res);
    }

    private static async Task WriteAsync(HttpContext http, JsonNode n)
    {
        http.Response.ContentType = "application/json";
        await http.Response.WriteAsync(n.ToJsonString());
    }

    private async Task<JsonNode?> DispatchAsync(JsonObject? m, string token, CancellationToken ct)
    {
        if (m is null) return Error(null, -32600, "Invalid request");
        var id = m["id"]?.DeepClone();
        var method = m["method"]?.ToString() ?? "";
        if (id is null) return null;   // notifications (initialized, cancelled…) need no answer
        try
        {
            return method switch
            {
                "initialize" => Result(id, new JsonObject
                {
                    ["protocolVersion"] = Supported.Contains(m["params"]?["protocolVersion"]?.ToString()) ? m["params"]!["protocolVersion"]!.ToString() : Protocol,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JsonObject { ["name"] = "mlplatform", ["title"] = "MLPlatform", ["version"] = "1" },
                    ["instructions"] = "MLPlatform: datasets, labeling (with AI proposals), training, model registry/conversion, deployments, jobs and machines. "
                        + "Call list_workspaces first; workspace_id defaults to the first workspace. Labels use normalized boxes (x,y,w,h in 0..1).",
                }),
                "ping" => Result(id, new JsonObject()),
                "tools/list" => Result(id, new JsonObject { ["tools"] = new JsonArray(Tools.Where(t => Find(t) is not null).Select(t => (JsonNode)Describe(t)).ToArray()) }),
                "tools/call" => Result(id, await CallAsync(m["params"] as JsonObject, token, ct)),
                _ => Error(id, -32601, $"Method not found: {method}"),
            };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "MCP {Method} failed", method);
            return Error(id, -32603, e.Message);
        }
    }

    /// <summary>A proto file may declare several services: the method is looked up in every service of the tool's file.</summary>
    private static MethodDescriptor? Find(Tool t) =>
        t.Service.FindMethodByName(t.Method) ?? t.Service.File.Services.Select(s => s.FindMethodByName(t.Method)).FirstOrDefault(m => m is not null);

    private static JsonObject Describe(Tool t)
    {
        var input = Find(t)!.InputType;
        var schema = Schema(input, 0);
        return new JsonObject
        {
            ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = schema,
            ["annotations"] = new JsonObject { ["readOnlyHint"] = t.ReadOnly, ["destructiveHint"] = false, ["openWorldHint"] = false },
        };
    }

    /// <summary>JSON Schema of a proto message (field names as in the proto; the platform accepts them as they are).</summary>
    private static JsonObject Schema(MessageDescriptor d, int depth)
    {
        if (d.FullName == "google.protobuf.Timestamp") return new JsonObject { ["type"] = "string", ["format"] = "date-time" };
        var props = new JsonObject();
        foreach (var f in d.Fields.InDeclarationOrder())
        {
            JsonNode one = f.FieldType switch
            {
                FieldType.String or FieldType.Bytes => new JsonObject { ["type"] = "string" },
                FieldType.Bool => new JsonObject { ["type"] = "boolean" },
                FieldType.Double or FieldType.Float => new JsonObject { ["type"] = "number" },
                FieldType.Enum => new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(f.EnumType.Values.Select(v => (JsonNode)v.Name).ToArray()) },
                FieldType.Message when f.IsMap => new JsonObject { ["type"] = "object" },
                FieldType.Message => depth < 3 ? Schema(f.MessageType, depth + 1) : new JsonObject { ["type"] = "object" },
                _ => new JsonObject { ["type"] = "integer" },
            };
            props[f.Name] = f.IsRepeated && !f.IsMap ? new JsonObject { ["type"] = "array", ["items"] = one } : one;
        }
        return new JsonObject { ["type"] = "object", ["properties"] = props };
    }

    private async Task<JsonObject> CallAsync(JsonObject? p, string token, CancellationToken ct)
    {
        var name = p?["name"]?.ToString() ?? "";
        if (!ByName.TryGetValue(name, out var tool)) return ToolError($"Unknown tool: {name}");
        var md = Find(tool);
        if (md is null) return ToolError($"Tool {name} is not available on this server");
        var args = p?["arguments"] as JsonObject ?? [];
        var headers = new Metadata { { "authorization", "Bearer " + token } };
        // workspace_id defaults to the caller's first workspace
        if (md.InputType.FindFieldByName("workspace_id") is not null && string.IsNullOrEmpty(args["workspace_id"]?.ToString()) && string.IsNullOrEmpty(args["workspaceId"]?.ToString()))
        {
            var ws = (ListWorkspacesResponse)await InvokeAsync(Find(ByName["list_workspaces"])!, new Empty(), headers, ct);
            if (ws.Workspaces.Count > 0) args["workspace_id"] = ws.Workspaces[0].Id;
        }
        IMessage req;
        try { req = JsonParser.Default.Parse(args.ToJsonString(), md.InputType); }
        catch (Exception e) when (e is InvalidProtocolBufferException or InvalidJsonException or FormatException) { return ToolError("Invalid arguments: " + e.Message); }
        try
        {
            var res = await InvokeAsync(md, req, headers, ct);
            var json = new JsonFormatter(JsonFormatter.Settings.Default.WithFormatDefaultValues(false)).Format(res);
            return new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = json }),
                ["structuredContent"] = JsonNode.Parse(json),
                ["isError"] = false,
            };
        }
        catch (RpcException e)
        {
            var field = string.Join("; ", e.Trailers.Where(t => t.Key.StartsWith("mlp-field-", StringComparison.Ordinal)).Select(t => $"{t.Key[10..]}: {Uri.UnescapeDataString(t.Value)}"));
            return ToolError($"{e.Status.StatusCode}: {e.Status.Detail}{(field.Length > 0 ? " (" + field + ")" : "")}");
        }
    }

    private async Task<IMessage> InvokeAsync(MethodDescriptor md, IMessage req, Metadata headers, CancellationToken ct)
    {
        var method = new Method<IMessage, IMessage>(MethodType.Unary, md.Service.FullName, md.Name,
            Marshallers.Create<IMessage>(m => m.ToByteArray(), b => md.InputType.Parser.ParseFrom(b)),
            Marshallers.Create<IMessage>(m => m.ToByteArray(), b => md.OutputType.Parser.ParseFrom(b)));
        return await _channel.CreateCallInvoker().AsyncUnaryCall(method, null, new CallOptions(headers, TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(2), ct), req);
    }

    private static JsonObject ToolError(string text) =>
        new() { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = true };

    private static JsonObject Result(JsonNode id, JsonNode result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
