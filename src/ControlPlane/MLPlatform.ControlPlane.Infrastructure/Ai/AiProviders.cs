using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Ai;
using MLPlatform.Cqrs;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Wire clients. Endpoint = API base the user entered (e.g. https://api.openai.com/v1, http://host:11434/v1, https://api.anthropic.com).</summary>
public sealed class AiProviders(IHttpClientFactory http)
{
    public sealed record ChatReply(string Text, string? Reasoning, string? StopReason, int TokensIn, int TokensOut, int Status);
    public sealed record DecisionReply(JsonObject Answers, int TokensIn, int TokensOut, int Status);

    public static string Protocol(string providerType) => providerType switch
    {
        "anthropic" => "anthropic",
        "systemone" or "typesafe" => "systemone",
        _ => "openai",
    };

    public static bool KeyRequired(string providerType) => providerType is "openai" or "anthropic" or "deepseek" or "typesafe";

    private static string Base(AiConnectionRow c) => c.Endpoint.TrimEnd('/');
    /// <summary>Decision servers: the user may paste the URL with or without /v1.</summary>
    private static string DecisionRoot(AiConnectionRow c) => Base(c).EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? Base(c)[..^3] : Base(c);

    /// <summary>How much state a decision model accepts (tokens ≈ chars / 3). Checked before sending, with a clear message.</summary>
    public static int StateTokenLimit(string model)
    {
        var name = model.Trim().ToLowerInvariant();
        return name.StartsWith("jev", StringComparison.Ordinal) ? 32_000 : name.StartsWith("nimble", StringComparison.Ordinal) ? 8_192 : 2_000;
    }
    private static string AnthropicBase(AiConnectionRow c) => Base(c).EndsWith("/v1", StringComparison.Ordinal) ? Base(c) : Base(c) + "/v1";

    private HttpRequestMessage Request(HttpMethod method, string url, AiConnectionRow c, string? key, string? body)
    {
        var m = new HttpRequestMessage(method, url);
        if (body is not null) m.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (Protocol(c.ProviderType) == "anthropic")
        {
            if (!string.IsNullOrEmpty(key)) m.Headers.Add("x-api-key", key);
            m.Headers.Add("anthropic-version", "2023-06-01");
        }
        else if (!string.IsNullOrEmpty(key)) m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return m;
    }

    private async Task<(int Status, string Body)> SendAsync(HttpRequestMessage m, CancellationToken ct)
    {
        var client = http.CreateClient("ai");
        HttpResponseMessage res;
        try { res = await client.SendAsync(m, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new AiCallException("timeout", "No answer before the deadline"); }
        catch (HttpRequestException e) { throw new AiCallException("unreachable", e.InnerException?.Message ?? e.Message); }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            var status = (int)res.StatusCode;
            if (res.IsSuccessStatusCode) return (status, text);
            var outcome = res.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "auth",
                HttpStatusCode.TooManyRequests => "rate-limited",
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "timeout",
                HttpStatusCode.NotFound => "not-found",
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "rejected",
                _ when status >= 500 => "server-error",
                _ => "rejected",
            };
            throw new AiCallException(outcome, $"HTTP {status}: {ErrorText(text)}", status);
        }
    }

    private static string ErrorText(string body)
    {
        try
        {
            var n = JsonNode.Parse(body);
            var e = n?["error"];
            var msg = e is JsonObject eo ? eo["message"]?.ToString() : e?.ToString();
            msg ??= n?["detail"]?.ToString() ?? n?["message"]?.ToString();
            if (!string.IsNullOrWhiteSpace(msg)) return Clip(msg, 300);
        }
        catch (JsonException) { }
        return Clip(body, 300);
    }

    public static string Clip(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    public async Task<ChatReply> ChatAsync(AiConnectionRow c, string? key, string model, IReadOnlyList<AiMessage> messages, int maxTokens, CancellationToken ct)
    {
        if (Protocol(c.ProviderType) == "systemone") throw new AiCallException("rejected", "A decision server cannot answer chat prompts");
        if (Protocol(c.ProviderType) == "anthropic")
        {
            var msgs = new JsonArray();
            foreach (var m in messages.Where(m => m.Role != "system"))
            {
                if (m.Images is not { Count: > 0 }) { msgs.Add((JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content }); continue; }
                var parts = new JsonArray();
                foreach (var img in m.Images)
                    parts.Add((JsonNode)new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = img.Mime, ["data"] = Convert.ToBase64String(img.Data) } });
                parts.Add((JsonNode)new JsonObject { ["type"] = "text", ["text"] = m.Content });
                msgs.Add((JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = parts });
            }
            var body = new JsonObject { ["model"] = model, ["max_tokens"] = maxTokens, ["messages"] = msgs };
            var sys = string.Join("\n\n", messages.Where(m => m.Role == "system").Select(m => m.Content));
            if (sys.Length > 0) body["system"] = sys;
            var (status, text) = await SendAsync(Request(HttpMethod.Post, AnthropicBase(c) + "/messages", c, key, body.ToJsonString()), ct);
            try
            {
                var n = JsonNode.Parse(text)!;
                var blocks = n["content"] as JsonArray ?? [];
                var reply = string.Concat(blocks.Where(b => b?["type"]?.ToString() == "text").Select(b => b?["text"]?.ToString()));
                var thinking = string.Concat(blocks.Where(b => b?["type"]?.ToString() == "thinking").Select(b => b?["thinking"]?.ToString()));
                return new ChatReply(reply, thinking.Length > 0 ? thinking : null, n["stop_reason"]?.ToString(),
                    n["usage"]?["input_tokens"]?.GetValue<int>() ?? 0, n["usage"]?["output_tokens"]?.GetValue<int>() ?? 0, status);
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or NullReferenceException)
            {
                throw new AiCallException("bad-response", "The answer is not valid Anthropic JSON: " + Clip(text, 200), status);
            }
        }
        else
        {
            var msgs = new JsonArray();
            foreach (var m in messages)
            {
                if (m.Images is not { Count: > 0 }) { msgs.Add((JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content }); continue; }
                var parts = new JsonArray { (JsonNode)new JsonObject { ["type"] = "text", ["text"] = m.Content } };
                foreach (var img in m.Images)
                    parts.Add((JsonNode)new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{img.Mime};base64,{Convert.ToBase64String(img.Data)}" } });
                msgs.Add((JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = parts });
            }
            var body = new JsonObject { ["model"] = model, ["messages"] = msgs, ["max_tokens"] = maxTokens, ["temperature"] = 0, ["stream"] = false };
            var (status, text) = await SendAsync(Request(HttpMethod.Post, Base(c) + "/chat/completions", c, key, body.ToJsonString()), ct);
            try
            {
                var n = JsonNode.Parse(text)!;
                var msg = n["choices"]?[0]?["message"] ?? throw new JsonException("choices[0].message missing");
                var reasoning = msg["reasoning"]?.ToString() ?? msg["reasoning_content"]?.ToString();
                return new ChatReply(msg["content"]?.ToString() ?? "", string.IsNullOrEmpty(reasoning) ? null : reasoning, n["choices"]?[0]?["finish_reason"]?.ToString(),
                    n["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0, n["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0, status);
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                throw new AiCallException("bad-response", "The answer is not valid chat-completions JSON: " + Clip(text, 200), status);
            }
        }
    }

    /// <summary>System One wire format: POST /v1/systemone {model, state, questions{id:{type,instructions,criteria}}}.</summary>
    public static JsonObject DecisionBody(string model, string state, IReadOnlyList<AiQuestion> questions)
    {
        var qs = new JsonObject();
        foreach (var q in questions)
        {
            var e = new JsonObject { ["type"] = q.Type, ["instructions"] = q.Instructions };
            if (q.Type == "score") e["criteria"] = new JsonArray([.. q.Options.Select(o => (JsonNode)JsonValue.Create(o.Description.Length > 0 ? o.Description : o.Key))]);
            else if (q.Options.Count > 0)
            {
                var crit = new JsonObject();
                foreach (var o in q.Options) crit[o.Key] = o.Description.Length > 0 ? o.Description : o.Key;
                e["criteria"] = crit;
            }
            qs[q.Id] = e;
        }
        return new JsonObject { ["model"] = model, ["state"] = state, ["questions"] = qs };
    }

    public async Task<DecisionReply> DecideAsync(AiConnectionRow c, string? key, string model, string state, IReadOnlyList<AiQuestion> questions, CancellationToken ct)
    {
        if (Protocol(c.ProviderType) != "systemone") throw new AiCallException("rejected", "Only decision servers answer typed questions");
        var stateTokens = (state.Length + 2) / 3;
        if (stateTokens > StateTokenLimit(model))
            throw new AiCallException("rejected", $"The state is about {stateTokens} tokens; {model} accepts at most {StateTokenLimit(model)}");
        var (status, text) = await SendAsync(Request(HttpMethod.Post, DecisionRoot(c) + "/v1/systemone", c, key, DecisionBody(model, state, questions).ToJsonString()), ct);
        try
        {
            var n = JsonNode.Parse(text)!;
            var answers = new JsonObject();
            foreach (var q in questions)
            {
                var a = n["answers"]?[q.Id] ?? throw new JsonException($"answer '{q.Id}' missing");
                var o = new JsonObject();
                switch (q.Type)
                {
                    case "score": o["score"] = a["score"]?.GetValue<double>() ?? throw new JsonException($"score '{q.Id}' missing"); break;
                    case "noul": o["noul"] = a["noul"]?.GetValue<double>() ?? throw new JsonException($"noul '{q.Id}' missing"); break;
                    default:
                        var choice = a["choice"]?.ToString() ?? throw new JsonException($"choice '{q.Id}' missing");
                        if (q.Options.Count > 0 && q.Options.All(x => x.Key != choice)) throw new JsonException($"choice '{choice}' is not one of the options");
                        o["choice"] = choice;
                        break;
                }
                if (a["confidence"] is JsonValue cv && cv.TryGetValue<double>(out var conf)) o["confidence"] = conf;
                if (a["probabilities"] is JsonObject p) o["probabilities"] = p.DeepClone();
                answers[q.Id] = o;
            }
            return new DecisionReply(answers, n["usage"]?["input_tokens"]?.GetValue<int>() ?? 0, n["usage"]?["output_tokens"]?.GetValue<int>() ?? 0, status);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            throw new AiCallException("bad-response", "The decision answer is invalid: " + e.Message, status);
        }
    }

    public async Task<List<string>> ListModelsAsync(AiConnectionRow c, string? key, CancellationToken ct)
    {
        var url = Protocol(c.ProviderType) switch { "anthropic" => AnthropicBase(c) + "/models", "systemone" => DecisionRoot(c) + "/v1/models", _ => Base(c) + "/models" };
        var (status, text) = await SendAsync(Request(HttpMethod.Get, url, c, key, null), ct);
        try
        {
            var n = JsonNode.Parse(text);
            var list = n?["data"] as JsonArray ?? n?["models"] as JsonArray ?? [];
            return [.. list.Select(x => x?["id"]?.ToString() ?? x?["name"]?.ToString() ?? x?["model"]?.ToString()).OfType<string>().Distinct().Order()];
        }
        catch (JsonException) { throw new AiCallException("bad-response", "The model list is not valid JSON", status); }
    }
}
