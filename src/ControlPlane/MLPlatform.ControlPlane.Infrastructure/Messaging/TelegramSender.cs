using System.Net.Mail;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Sends Telegram messages using the "telegram" dynamic settings.</summary>
public sealed class TelegramSender(DynamicSettings settings, IHttpClientFactory http)
{
    public async Task<bool> SendAsync(string text, CancellationToken ct, string? chatId = null)
    {
        if (await settings.GetAsync("telegram", ct) is not { } s) return false;
        await SendWithAsync(http, s, text, chatId, ct);
        return true;
    }

    public static async Task SendWithAsync(IHttpClientFactory http, ResolvedSetting s, string text, string? chatId, CancellationToken ct)
    {
        var baseUrl = s.Get("api_base") is { Length: > 0 } b ? b.TrimEnd('/') : "https://api.telegram.org";
        var body = new System.Text.Json.Nodes.JsonObject { ["chat_id"] = string.IsNullOrWhiteSpace(chatId) ? s.Get("chat_id") : chatId.Trim(), ["text"] = text };
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/bot{s.Get("bot_token")}/sendMessage")
            { Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
        using var res = await http.CreateClient("telegram").SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct);
            var desc = System.Text.Json.Nodes.JsonNode.Parse(err)?["description"]?.ToString() ?? err;
            throw new InvalidOperationException($"Telegram returned error {(int)res.StatusCode}: {desc}");
        }
    }
}
