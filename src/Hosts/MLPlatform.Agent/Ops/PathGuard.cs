namespace MLPlatform.Agent.Ops;

/// <summary>Keeps remote file operations inside the allowed roots, resolving every symlink component of the
/// path.</summary>
public static class PathGuard
{
    private static readonly StringComparison Cmp =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static List<string> Roots(AgentOptions o, string stateDir) =>
        [.. new[] { o.DataPath, o.ModelsPath, stateDir }.Concat(o.FsRoots)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => Real(Path.GetFullPath(r)))
            .Distinct(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)];

    /// <summary>The real absolute path when it lies inside one of the roots; null otherwise.</summary>
    public static string? Resolve(IReadOnlyList<string> roots, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        var real = Real(Path.GetFullPath(path));
        return roots.Any(r => Inside(r, real)) ? real : null;
    }

    public static bool IsRoot(IReadOnlyList<string> roots, string real) => roots.Any(r => string.Equals(r.TrimEnd(Path.DirectorySeparatorChar), real.TrimEnd(Path.DirectorySeparatorChar), Cmp));

    public static bool Inside(string root, string real)
    {
        var r = root.TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(r, real.TrimEnd(Path.DirectorySeparatorChar), Cmp) || real.StartsWith(r + Path.DirectorySeparatorChar, Cmp);
    }

    /// <summary>Resolves symlinks component by component; components that do not exist yet are kept as written.</summary>
    public static string Real(string full)
    {
        var root = Path.GetPathRoot(full) ?? "";
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, part);
            current = LinkTarget(next) is { } target ? Real(Path.GetFullPath(target, current)) : next;
        }
        return current;
    }

    private static string? LinkTarget(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.Exists && info.LinkTarget is { } t ? t : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
