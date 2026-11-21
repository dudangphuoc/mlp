using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Infrastructure;

/// <summary>Definition of one settings field.</summary>
public sealed record SettingFieldDef(string Key, string Label, string Type = "text", bool Required = false, string Hint = "", string[]? Options = null, string Default = "")
{
    public bool Secret => Type == "secret";
}

/// <summary>Definition of one dynamic settings group.</summary>
public sealed record SettingDef(string Key, string Title, string Description, string TestHint, SettingFieldDef[] Fields);

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

/// <summary>Decrypted settings (plain fields + secrets combined).</summary>
public sealed record ResolvedSetting(bool Enabled, IReadOnlyDictionary<string, string> Values)
{
    public string Get(string k) => Values.TryGetValue(k, out var v) ? v : "";
}

/// <summary>Reads dynamic settings from the DB, cached 15 s (changes in the UI take effect almost immediately).</summary>
public sealed class DynamicSettings(IServiceScopeFactory scopes, SecretBox box, MLPlatform.Security.IClock clock)
{
    private readonly Dictionary<string, (DateTimeOffset At, ResolvedSetting? Value)> _cache = [];
    private readonly Lock _gate = new();

    public static Dictionary<string, string> ParseMap(string? json)
    {
        var d = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(json)) return d;
        try { if (JsonNode.Parse(json) is JsonObject o) foreach (var (k, v) in o) d[k] = v?.ToString() ?? ""; } catch (System.Text.Json.JsonException) { }
        return d;
    }

    public static string ToJson(IReadOnlyDictionary<string, string> m)
    {
        var o = new JsonObject();
        foreach (var (k, v) in m) o[k] = v;
        return o.ToJsonString();
    }

    public ResolvedSetting Resolve(SettingRow? row, SettingDef def)
    {
        var values = def.Fields.Where(f => !f.Secret).ToDictionary(f => f.Key, f => f.Default);
        if (row is null) return new(false, values);
        foreach (var (k, v) in ParseMap(row.ConfigJson)) values[k] = v;
        foreach (var (k, v) in ParseMap(box.Open(row.SecretsEnc))) values[k] = v;
        return new(row.Enabled, values);
    }

    public void Invalidate(string key) { lock (_gate) _cache.Remove(key); }

    public async Task<ResolvedSetting?> GetAsync(string key, CancellationToken ct)
    {
        lock (_gate) if (_cache.TryGetValue(key, out var c) && clock.UtcNow - c.At < TimeSpan.FromSeconds(15)) return c.Value;
        var def = SettingsCatalog.Find(key) ?? throw new ArgumentException(key);
        await using var scope = scopes.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<IMediator>().Query(new GetSettingInternalQuery(key), ct);
        var r = Resolve(row, def);
        var v = r.Enabled ? r : null;
        lock (_gate) _cache[key] = (clock.UtcNow, v);
        return v;
    }
}
