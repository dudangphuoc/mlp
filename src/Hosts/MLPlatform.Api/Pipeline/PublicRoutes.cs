using MLPlatform.ControlPlane.Infrastructure;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Infrastructure;

/// <summary>
/// Public subdomains under <c>Routes:Domain</c> (e.g. mlp.hexbox.vn, wildcard DNS + cert at Traefik):
///   nb-{id:N}.{domain}            → notebook (proxy /nb/{id}/…, notebook owner only; the session cookie is shared across *.{domain})
///   {slug}--{workspace}.{domain}  → deployment: POST /predict, /feedback (endpoint key mlpd_…)
/// </summary>
public sealed partial class PublicRoutes(IServiceScopeFactory scopes, MLPlatform.Security.IClock clock)
{
    public static string Domain { get; private set; } = "";
    private Dictionary<Guid, string> _byId = [];
    private Dictionary<string, Guid> _bySlug = [];
    private DateTimeOffset _at;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static void Configure(IConfiguration cfg) => Domain = (cfg["Routes:Domain"] ?? "").Trim().TrimStart('.').ToLowerInvariant();

    public static string NotebookUrl(Guid id, string fallback) => Domain.Length == 0 ? fallback : $"https://nb-{id:N}.{Domain}/";

    public async Task<string?> DeploymentUrlAsync(Guid ws, string slug, CancellationToken ct) =>
        Domain.Length > 0 && (await MapAsync(ct)).ById.TryGetValue(ws, out var w) ? $"https://{slug}--{w}.{Domain}" : null;

    private async Task<(Dictionary<Guid, string> ById, Dictionary<string, Guid> BySlug)> MapAsync(CancellationToken ct)
    {
        if (clock.UtcNow - _at < TimeSpan.FromSeconds(30)) return (_byId, _bySlug);
        await _gate.WaitAsync(ct);
        try
        {
            if (clock.UtcNow - _at >= TimeSpan.FromSeconds(30))
            {
                await using var scope = scopes.CreateAsyncScope();
                var rows = await scope.ServiceProvider.GetRequiredService<IMediator>().Query(new ListWorkspaceSlugsQuery(), ct);
                _byId = rows.ToDictionary(r => r.Id, r => r.Slug);
                _bySlug = rows.ToDictionary(r => r.Slug, r => r.Id, StringComparer.OrdinalIgnoreCase);
                _at = clock.UtcNow;
            }
        }
        finally { _gate.Release(); }
        return (_byId, _bySlug);
    }

    [GeneratedRegex(@"^nb-([0-9a-f]{32})$")] private static partial Regex NbHost();
    [GeneratedRegex(@"^([a-z0-9][a-z0-9-]*?)--([a-z0-9][a-z0-9-]*)$")] private static partial Regex DepHost();

    /// <summary>Middleware: rewrites the path by Host before routing. Other hosts → untouched.</summary>
    public async Task HandleAsync(HttpContext ctx, Func<Task> next)
    {
        var host = ctx.Request.Host.Host.ToLowerInvariant();
        if (Domain.Length > 0 && host.EndsWith("." + Domain, StringComparison.Ordinal))
        {
            var label = host[..^(Domain.Length + 1)];
            var path = ctx.Request.Path.Value ?? "/";
            if (NbHost().Match(label) is { Success: true } nb)
            {
                var id = Guid.ParseExact(nb.Groups[1].Value, "N");
                var prefix = $"/nb/{id}";
                if (path is "/" or "") { ctx.Response.Redirect($"{prefix}/lab"); return; }
                if (!path.StartsWith(prefix + "/", StringComparison.Ordinal) && !path.StartsWith("/mlplatform.v1.", StringComparison.Ordinal))
                    ctx.Request.Path = prefix + path;
            }
            else if (DepHost().Match(label) is { Success: true } dep && path.TrimEnd('/') is "/predict" or "/feedback"
                     && (await MapAsync(ctx.RequestAborted)).BySlug.TryGetValue(dep.Groups[2].Value, out var ws))
            {
                ctx.Request.Path = $"/v1/deployments/{ws}/{dep.Groups[1].Value}{path.TrimEnd('/')}";
            }
        }
        await next();
    }
}
