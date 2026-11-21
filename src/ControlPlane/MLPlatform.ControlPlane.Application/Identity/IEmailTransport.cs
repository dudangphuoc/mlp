using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>Email sending gateway (SMTP per the dynamic "email" config in api; when disabled → nothing is sent, emails
/// stay queued and the Admin copies links manually — D-27).</summary>
public interface IEmailTransport
{
    Task<bool> IsConfiguredAsync(CancellationToken ct);
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}
