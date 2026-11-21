using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

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
