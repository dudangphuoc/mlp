using System.Net.Mail;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

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
