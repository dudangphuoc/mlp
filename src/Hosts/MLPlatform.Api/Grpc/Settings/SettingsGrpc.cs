using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Grpc;

/// <summary>Dynamic instance settings (email, Telegram, …); Instance Admin only.</summary>
public sealed class SettingsGrpc(IMediator mediator, DynamicSettings settings, SecretBox box, IHttpClientFactory http) : SettingsService.SettingsServiceBase
{
    private static SettingDef Def(string key) => SettingsCatalog.Find(key)
        ?? throw new ValidationException(new Dictionary<string, string> { ["key"] = "Settings type does not exist" });

    private Setting Map(SettingDef def, SettingRow? row)
    {
        var r = settings.Resolve(row, def);
        var s = new Setting { Key = def.Key, Title = def.Title, Description = def.Description, Enabled = r.Enabled, TestHint = def.TestHint,
            UpdatedAt = row is null ? null : Timestamp.FromDateTimeOffset(row.UpdatedAt) };
        foreach (var f in def.Fields)
        {
            var v = r.Get(f.Key);
            var x = new SettingField { Key = f.Key, Label = f.Label, Type = f.Type, Secret = f.Secret, Required = f.Required, Hint = f.Hint,
                Value = f.Secret ? "" : v, HasValue = v.Length > 0 };
            if (f.Options is { } o) x.Options.AddRange(o);
            s.Fields.Add(x);
        }
        return s;
    }

    public override async Task<ListSettingsResponse> ListSettings(ListSettingsRequest r, ServerCallContext ctx)
    {
        var rows = (await mediator.Query(new ListSettingsQuery(), ctx.CancellationToken)).ToDictionary(x => x.Key);
        var res = new ListSettingsResponse();
        res.Settings.AddRange(SettingsCatalog.All.Select(d => Map(d, rows.GetValueOrDefault(d.Key))));
        return res;
    }

    public override async Task<Setting> UpdateSetting(UpdateSettingRequest r, ServerCallContext ctx)
    {
        var def = Def(r.Key);
        var rows = (await mediator.Query(new ListSettingsQuery(), ctx.CancellationToken)).ToDictionary(x => x.Key);
        var old = rows.GetValueOrDefault(def.Key);
        var plain = DynamicSettings.ParseMap(old?.ConfigJson);
        var secret = DynamicSettings.ParseMap(box.Open(old?.SecretsEnc));
        foreach (var f in def.Fields)
        {
            if (!r.Values.TryGetValue(f.Key, out var v)) continue;
            v = v.Trim();
            if (f.Secret) { if (v == "-") secret.Remove(f.Key); else if (v.Length > 0) secret[f.Key] = v; }
            else plain[f.Key] = v;
        }
        if (r.Enabled)
        {
            var errors = def.Fields.Where(f => f.Required && (f.Secret ? !secret.ContainsKey(f.Key) : plain.GetValueOrDefault(f.Key, f.Default).Length == 0))
                .ToDictionary(f => f.Key, f => $"{f.Label} is required");
            foreach (var f in def.Fields.Where(f => f.Type == "number" && plain.GetValueOrDefault(f.Key, "") is { Length: > 0 } n && !int.TryParse(n, out _)))
                errors[f.Key] = $"{f.Label} must be a number";
            if (errors.Count > 0) throw new ValidationException(errors);
        }
        await mediator.Send(new UpsertSettingCommand(def.Key, r.Enabled, DynamicSettings.ToJson(plain),
            secret.Count > 0 ? box.Seal(DynamicSettings.ToJson(secret)) : null), ctx.CancellationToken);
        settings.Invalidate(def.Key);
        rows = (await mediator.Query(new ListSettingsQuery(), ctx.CancellationToken)).ToDictionary(x => x.Key);
        return Map(def, rows.GetValueOrDefault(def.Key));
    }

    public override async Task<TestSettingResponse> TestSetting(TestSettingRequest r, ServerCallContext ctx)
    {
        var def = Def(r.Key);
        var row = (await mediator.Query(new ListSettingsQuery(), ctx.CancellationToken)).FirstOrDefault(x => x.Key == def.Key);
        if (row is null) return new TestSettingResponse { Ok = false, Message = "Settings have not been saved" };
        var s = settings.Resolve(row, def);
        try
        {
            switch (def.Key)
            {
                case "email":
                    if (string.IsNullOrWhiteSpace(r.To)) throw new ValidationException(new Dictionary<string, string> { ["to"] = "Recipient email is required" });
                    await SmtpTransport.SendWithAsync(s, r.To.Trim(), "MLPlatform test email", "MLPlatform email settings are working.", ctx.CancellationToken);
                    return new TestSettingResponse { Ok = true, Message = $"Sent to {r.To.Trim()}" };
                case "telegram":
                    await TelegramSender.SendWithAsync(http, s, "✅ MLPlatform: Telegram settings are working.", r.To, ctx.CancellationToken);
                    return new TestSettingResponse { Ok = true, Message = "Test message sent" };
                default:
                    return new TestSettingResponse { Ok = false, Message = "This type does not support test sending yet" };
            }
        }
        catch (Exception e) when (e is not (ValidationException or OperationCanceledException))
        {
            return new TestSettingResponse { Ok = false, Message = e.Message };
        }
    }
}
