using System.Text.RegularExpressions;

namespace MLPlatform.Agent.Gpu;

/// <summary>Maps a host pid to its Docker container id through /proc/&lt;pid&gt;/cgroup (Linux only).</summary>
public static partial class Cgroup
{
    [GeneratedRegex("(?:docker[-/]|/)([0-9a-f]{64})(?:\\.scope)?", RegexOptions.CultureInvariant)]
    private static partial Regex ContainerId();

    public static string? ContainerOf(int pid)
    {
        if (!OperatingSystem.IsLinux()) return null;
        try { return Parse(File.ReadAllText($"/proc/{pid}/cgroup")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static string? Parse(string cgroupFile) =>
        cgroupFile.Split('\n').Select(l => ContainerId().Match(l)).FirstOrDefault(m => m.Success)?.Groups[1].Value;
}
