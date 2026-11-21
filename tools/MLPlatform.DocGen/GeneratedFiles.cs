namespace MLPlatform.DocGen;

/// <summary>Writes generated files, or with --check fails when a committed file differs from what the code
/// produces.</summary>
public static class GeneratedFiles
{
    public static int Sync(string root, IReadOnlyDictionary<string, string> outputs, bool check)
    {
        var stale = new List<string>();
        foreach (var (relative, text) in outputs)
        {
            var path = Path.Combine(root, relative);
            var current = File.Exists(path) ? File.ReadAllText(path) : null;
            if (current == text) continue;
            stale.Add(relative);
            if (check) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        var orphans = OrphanDocs(root, outputs);
        if (!check) foreach (var orphan in orphans) File.Delete(Path.Combine(root, orphan));
        stale.AddRange(orphans);
        foreach (var file in stale) Console.WriteLine((check ? "STALE " : "wrote ") + file);
        Console.WriteLine($"docgen: {outputs.Count} files, {stale.Count} {(check ? "stale" : "updated")}");
        return check && stale.Count > 0 ? 1 : 0;
    }

    private static IEnumerable<string> OrphanDocs(string root, IReadOnlyDictionary<string, string> outputs)
    {
        var dir = Path.Combine(root, "docs", "generated");
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !outputs.ContainsKey(f))
            .ToList();
    }
}
