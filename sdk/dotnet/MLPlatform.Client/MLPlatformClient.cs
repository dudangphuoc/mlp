// MLPlatform client for .NET — call a deployed model from your application.
// One file, no packages beyond System.Text.Json (in the box since .NET Core 3.0). Copy it into your project or reference MLPlatform.Client.csproj.
//
//     using var model = new MLPlatformClient("https://<endpoint>/predict", "mlpd_...");   // address and key from the deployment's page
//     var result = await model.DetectAsync("photo.jpg", conf: 0.25);
//     foreach (var o in result.Objects) Console.WriteLine($"{o.Class} {o.Confidence:F2} [{o.Box.X1},{o.Box.Y1},{o.Box.X2},{o.Box.Y2}]");
//
// Other tasks: ClassifyAsync, TextAsync, TabularAsync, ChatAsync; PredictAsync sends raw inputs.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MLPlatform.Client
{
    /// <summary>The platform refused or failed the call. Status = HTTP status, Reason = stable code (e.g. SERVE_KEY_INVALID).</summary>
    public sealed class MLPlatformException : Exception
    {
        public int Status { get; }
        public string Reason { get; }
        public MLPlatformException(int status, string reason, string message) : base(reason.Length > 0 ? reason + ": " + message : message)
        {
            Status = status;
            Reason = reason;
        }
    }

    public sealed class Box
    {
        [JsonPropertyName("x1")] public double X1 { get; set; }
        [JsonPropertyName("y1")] public double Y1 { get; set; }
        [JsonPropertyName("x2")] public double X2 { get; set; }
        [JsonPropertyName("y2")] public double Y2 { get; set; }
    }

    public sealed class DetectedObject
    {
        [JsonPropertyName("class")] public string Class { get; set; } = "";
        [JsonPropertyName("classId")] public int ClassId { get; set; }
        [JsonPropertyName("confidence")] public double Confidence { get; set; }
        [JsonPropertyName("box")] public Box Box { get; set; } = new Box();
    }

    public sealed class ImageSize
    {
        [JsonPropertyName("width")] public int Width { get; set; }
        [JsonPropertyName("height")] public int Height { get; set; }
    }

    public sealed class Detection
    {
        [JsonPropertyName("imageSize")] public ImageSize ImageSize { get; set; } = new ImageSize();
        [JsonPropertyName("objects")] public List<DetectedObject> Objects { get; set; } = new List<DetectedObject>();
    }

    public sealed class LabelScore
    {
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("score")] public double Score { get; set; }
    }

    public sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "user";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
        public ChatMessage() { }
        public ChatMessage(string role, string content) { Role = role; Content = content; }
    }

    public sealed class ChatResult
    {
        public string Text { get; set; } = "";
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public double LatencyMs { get; set; }
        public IReadOnlyList<string> Flags { get; set; } = Array.Empty<string>();
    }

    /// <summary>A deployed model. endpoint = the deployment's predict address, key = an endpoint key (mlpd_…).</summary>
    public sealed class MLPlatformClient : IDisposable
    {
        private readonly HttpClient http;
        private readonly bool ownsHttp;
        private readonly string endpoint;

        /// <summary>Ids of the last predict call, for <see cref="FeedbackAsync"/>.</summary>
        public IReadOnlyList<string> LastPredictionIds { get; private set; } = Array.Empty<string>();
        /// <summary>The model version that answered the last call, e.g. "fire-detector@v3".</summary>
        public string LastModelVersion { get; private set; } = "";

        public MLPlatformClient(string endpoint, string key, HttpClient? httpClient = null)
        {
            var url = endpoint.TrimEnd('/');
            this.endpoint = url.EndsWith("/predict", StringComparison.Ordinal) ? url : url + "/predict";
            ownsHttp = httpClient is null;
            http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        /// <summary>Object detection on an image file. labels = the classes to look for, for zero-shot models.</summary>
        public Task<Detection> DetectAsync(string imagePath, double conf = 0.25, IEnumerable<string>? labels = null, CancellationToken ct = default) =>
            DetectAsync(File.ReadAllBytes(imagePath), conf, labels, ct);

        public async Task<Detection> DetectAsync(byte[] image, double conf = 0.25, IEnumerable<string>? labels = null, CancellationToken ct = default) =>
            (await PredictAsync(new[] { ImageInput(image, conf, labels) }, ct).ConfigureAwait(false))[0].Deserialize<Detection>() ?? new Detection();

        /// <summary>Image classification: labels with scores, best first.</summary>
        public async Task<List<LabelScore>> ClassifyAsync(byte[] image, IEnumerable<string>? labels = null, CancellationToken ct = default) =>
            (await PredictAsync(new[] { ImageInput(image, null, labels) }, ct).ConfigureAwait(false))[0].Deserialize<List<LabelScore>>() ?? new List<LabelScore>();

        /// <summary>Text classification or entity recognition on one text; the shape depends on the model's task.</summary>
        public async Task<JsonNode?> TextAsync(string text, IEnumerable<string>? labels = null, CancellationToken ct = default)
        {
            var item = new JsonObject { ["text"] = text };
            if (labels != null) item["labels"] = new JsonArray(labels.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray());
            return (await PredictAsync(new[] { item }, ct).ConfigureAwait(false))[0];
        }

        /// <summary>One row of features → {"label","probabilities"} or {"value"}.</summary>
        public async Task<JsonNode?> TabularAsync(IDictionary<string, object?> row, CancellationToken ct = default) =>
            (await PredictAsync(new[] { new JsonObject { ["tabular"] = JsonSerializer.SerializeToNode(row) } }, ct).ConfigureAwait(false))[0];

        /// <summary>Text generation.</summary>
        public async Task<ChatResult> ChatAsync(IEnumerable<ChatMessage> messages, int maxTokens = 512, double temperature = 0.7, CancellationToken ct = default)
        {
            var body = new JsonObject { ["messages"] = JsonSerializer.SerializeToNode(messages.ToList()), ["max_tokens"] = maxTokens, ["temperature"] = temperature };
            var res = await PostAsync(endpoint, body, ct).ConfigureAwait(false);
            var usage = res["usage"];
            return new ChatResult
            {
                Text = res["output"]?["text"]?.ToString() ?? "",
                PromptTokens = (int)(usage?["prompt_tokens"]?.GetValue<double>() ?? 0),
                CompletionTokens = (int)(usage?["completion_tokens"]?.GetValue<double>() ?? 0),
                LatencyMs = usage?["latency_ms"]?.GetValue<double>() ?? 0,
                Flags = (res["flags"] as JsonArray)?.Select(f => f?.ToString() ?? "").ToList() ?? new List<string>(),
            };
        }

        /// <summary>Several inputs in one call; each is {"image_b64"…} | {"text"…} | {"tabular"…}. One prediction per input.</summary>
        public async Task<IReadOnlyList<JsonNode?>> PredictAsync(IEnumerable<JsonObject> inputs, CancellationToken ct = default)
        {
            var res = await PostAsync(endpoint, new JsonObject { ["inputs"] = new JsonArray(inputs.Select(i => (JsonNode?)i).ToArray()) }, ct).ConfigureAwait(false);
            return (res["predictions"] as JsonArray)?.ToList() ?? new List<JsonNode?>();
        }

        /// <summary>Send the true answer of an earlier prediction (a label or a number). Returns how many were accepted.</summary>
        public async Task<int> FeedbackAsync(IEnumerable<(string PredictionId, string? Label, double? Value)> items, CancellationToken ct = default)
        {
            var list = new JsonArray();
            foreach (var (id, label, value) in items)
            {
                var o = new JsonObject { ["prediction_id"] = id };
                if (label != null) o["label"] = label; else if (value != null) o["value"] = value.Value;
                list.Add(o);
            }
            var url = endpoint.Substring(0, endpoint.Length - "/predict".Length) + "/feedback";
            return (int)((await PostAsync(url, new JsonObject { ["feedback"] = list }, ct).ConfigureAwait(false))["accepted"]?.GetValue<double>() ?? 0);
        }

        private static JsonObject ImageInput(byte[] image, double? conf, IEnumerable<string>? labels)
        {
            var item = new JsonObject { ["image_b64"] = Convert.ToBase64String(image) };
            if (conf != null) item["conf"] = conf.Value;
            if (labels != null) item["labels"] = new JsonArray(labels.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray());
            return item;
        }

        private async Task<JsonNode> PostAsync(string url, JsonObject body, CancellationToken ct)
        {
            HttpResponseMessage res;
            try
            {
                using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                res = await http.PostAsync(url, content, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException e) { throw new MLPlatformException(0, "UNREACHABLE", e.Message); }
            using (res)
            {
                var raw = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonNode? json = null;
                try { json = raw.Length > 0 ? JsonNode.Parse(raw) : null; } catch (JsonException) { }
                if (!res.IsSuccessStatusCode)
                {
                    var err = json?["error"];
                    throw new MLPlatformException((int)res.StatusCode, err?["reason"]?.ToString() ?? "", err?["message"]?.ToString() ?? (raw.Length > 300 ? raw.Substring(0, 300) : raw));
                }
                LastPredictionIds = res.Headers.TryGetValues("X-Mlp-Prediction-Ids", out var ids)
                    ? string.Join(",", ids).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                LastModelVersion = res.Headers.TryGetValues("X-Mlp-Model-Version", out var mv) ? string.Join(",", mv) : "";
                return json ?? new JsonObject();
            }
        }

        public void Dispose() { if (ownsHttp) http.Dispose(); }
    }
}
