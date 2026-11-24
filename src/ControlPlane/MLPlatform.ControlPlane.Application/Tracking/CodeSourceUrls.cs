namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Git remote URLs in one comparable form (https://host/owner/repo) and longest-prefix matching.</summary>
public static class CodeSourceUrls
{
    public static string Normalize(string url)
    {
        var u = url.Trim();
        if (!u.Contains("://", StringComparison.Ordinal) && u.Contains('@') && u.Contains(':'))
            u = "https://" + u[(u.IndexOf('@') + 1)..].Replace(':', '/');
        if (u.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("git://", StringComparison.OrdinalIgnoreCase))
            u = "https://" + u[(u.IndexOf("://", StringComparison.Ordinal) + 3)..];
        if (!Uri.TryCreate(u, UriKind.Absolute, out var parsed)) return url.Trim();
        var path = parsed.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return $"https://{parsed.Host.ToLowerInvariant()}{path}";
    }

    public static CodeSourceRow? Match(IEnumerable<CodeSourceRow> sources, string remote)
    {
        var target = Normalize(remote);
        return sources
            .Where(s => target.Equals(s.UrlPrefix, StringComparison.OrdinalIgnoreCase) || target.StartsWith(s.UrlPrefix + "/", StringComparison.OrdinalIgnoreCase))
            .MaxBy(s => s.UrlPrefix.Length);
    }

    public static (string Owner, string Repo) OwnerRepo(string remote)
    {
        var parts = new Uri(Normalize(remote)).AbsolutePath.Trim('/').Split('/');
        return parts.Length >= 2 ? (string.Join('/', parts[..^1]), parts[^1]) : ("", parts[0]);
    }
}
