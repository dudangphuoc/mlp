namespace MLPlatform.CodeGate;

public sealed class RepoFiles(string root)
{
    public string Root { get; } = root;

    public string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    public IEnumerable<string> Under(string directory, string pattern) =>
        Directory.Exists(Path.Combine(Root, directory))
            ? Directory.EnumerateFiles(Path.Combine(Root, directory), pattern, SearchOption.AllDirectories).Where(IsSource).Order(StringComparer.Ordinal)
            : [];

    public IEnumerable<string> HandWrittenCSharp() => [.. Under("src", "*.cs"), .. Under("tools", "*.cs")];

    private static bool IsSource(string path) =>
        !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal)
        && !path.Contains("/node_modules/", StringComparison.Ordinal);
}
