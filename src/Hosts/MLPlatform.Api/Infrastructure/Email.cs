using System.Net.Mail;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Infrastructure;

/// <summary>SMTP from the "email" dynamic settings (Settings → System). Not enabled → nothing is sent (D-27).</summary>
public sealed class SmtpTransport(DynamicSettings settings) : IEmailTransport
{
    public async Task<bool> IsConfiguredAsync(CancellationToken ct) => (await settings.GetAsync("email", ct)) is { } s && s.Get("host").Length > 0;

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var s = await settings.GetAsync("email", ct) ?? throw new InvalidOperationException("Email is not configured");
        await SendWithAsync(s, to, subject, body, ct);
    }

    public static async Task SendWithAsync(ResolvedSetting s, string to, string subject, string body, CancellationToken ct)
    {
        using var client = new SmtpClient(s.Get("host"), int.TryParse(s.Get("port"), out var p) ? p : 587)
            { EnableSsl = s.Get("ssl") is "true" or "1", Timeout = 20000 };
        if (s.Get("user") is { Length: > 0 } user) client.Credentials = new System.Net.NetworkCredential(user, s.Get("password"));
        var from = s.Get("from") is { Length: > 0 } f ? f : "mlplatform@localhost";
        using var msg = new MailMessage(new MailAddress(from), new MailAddress(to)) { Subject = subject, Body = body,
            BodyEncoding = System.Text.Encoding.UTF8, SubjectEncoding = System.Text.Encoding.UTF8 };
        await client.SendMailAsync(msg, ct);
    }
}

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

/// <summary>Email sending loop every 5 s (P0-18).</summary>
public sealed class EmailSender(IServiceScopeFactory scopes, IConfiguration cfg, SystemHealth health, ILogger<EmailSender> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stop))
        {
            health.Beat("email", 5);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var n = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new DispatchEmailsCommand(cfg["PublicUrl"] ?? ""), stop);
                if (n > 0) log.LogInformation("Sent {N} email(s)", n);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning("Sending email failed: {Msg}", e.Message); }
        }
    }
}
