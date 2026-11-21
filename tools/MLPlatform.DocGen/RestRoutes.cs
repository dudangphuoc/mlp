using System.Text.RegularExpressions;

namespace MLPlatform.DocGen;

/// <summary>REST routes mapped under Api/Http; the area is the folder that holds the endpoint.</summary>
public static partial class RestRoutes
{
    private static readonly HashSet<string> PublicAreas = ["Inference", "Health", "Install"];

    public static IReadOnlyList<RestRoute> Scan(string root)
    {
        var dir = Path.Combine(root, "src/Hosts/MLPlatform.Api/Http");
        var routes = new List<RestRoute>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var area = Path.GetRelativePath(dir, file).Split(Path.DirectorySeparatorChar) is { Length: > 1 } parts ? parts[0] : "Http";
            foreach (Match m in Mapping().Matches(File.ReadAllText(file)))
                routes.Add(new RestRoute(m.Groups[1].Value is "" ? "ANY" : m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value, area, PublicAreas.Contains(area)));
        }
        return [.. routes.OrderBy(r => r.Path, StringComparer.Ordinal).ThenBy(r => r.Verb, StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"\.Map(Get|Post|Put|Delete|Patch|)\(\s*""([^""]+)""")]
    private static partial Regex Mapping();
}
