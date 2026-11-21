using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Dynamic settings catalog. Adding a new provider = adding a <see cref="SettingDef"/> here.</summary>
public static class SettingsCatalog
{
    public static readonly SettingDef[] All =
    [
        new("email", "Email (SMTP)", "Sends invitation, password reset and notification emails. When off, emails stay queued and an Admin copies links manually.", "Test recipient email",
        [
            new("host", "SMTP host", Required: true, Hint: "e.g. smtp.gmail.com"),
            new("port", "Port", "number", true, "587 (STARTTLS) / 465 (SSL) / 25", Default: "587"),
            new("ssl", "Use TLS", "bool", Default: "true"),
            new("user", "Username"),
            new("password", "Password", "secret"),
            new("from", "Sender address (From)", Required: true, Hint: "e.g. MLPlatform <noreply@example.com>"),
        ]),
        new("telegram", "Telegram", "Bot that sends system alerts to a Telegram group/channel.", "Test chat ID (empty = default chat)",
        [
            new("bot_token", "Bot token", "secret", true, "Get it from @BotFather"),
            new("chat_id", "Default chat ID", Required: true, Hint: "e.g. -1001234567890"),
            new("api_base", "API base", Hint: "Leave empty = https://api.telegram.org", Default: ""),
        ]),
    ];

    public static SettingDef? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}
