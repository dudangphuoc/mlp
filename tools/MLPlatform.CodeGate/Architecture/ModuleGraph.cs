using System.Text.RegularExpressions;

namespace MLPlatform.CodeGate.Architecture;

public sealed record ModuleReference(string From, string To, string File, string Kind, string Target);

/// <summary>Cross-module references in Application: namespace imports, qualified names and calls to another module's SQL methods.</summary>
public static partial class ModuleGraph
{
    private const string NamespacePrefix = "MLPlatform.ControlPlane.Application.";

    public static IReadOnlyList<ModuleReference> Build(RepoFiles repo, ModuleManifest manifest)
    {
        var files = repo.Under(ModuleManifest.ApplicationRoot, "*.cs")
            .Select(f => (Path: repo.Relative(f), Module: ModuleOf(repo.Relative(f)), Text: StripComments(File.ReadAllText(f))))
            .Where(f => f.Module.Length > 0)
            .ToList();
        var modules = files.Select(f => f.Module).ToHashSet(StringComparer.Ordinal);
        var sqlOwners = SqlMethodOwners(files);
        var references = new List<ModuleReference>();
        foreach (var (path, module, text) in files)
        {
            foreach (Match m in NamespaceReference().Matches(text))
                if (m.Groups[1].Value is var target && target != module && modules.Contains(target))
                    references.Add(new(module, target, path, "namespace", target));
            foreach (Match m in SqlCall().Matches(text))
                if (sqlOwners.TryGetValue(m.Groups[1].Value, out var owner) && owner != module)
                    references.Add(new(module, owner, path, "sql", m.Groups[1].Value));
        }
        return references;
    }

    private static Dictionary<string, string> SqlMethodOwners(IEnumerable<(string Path, string Module, string Text)> files)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, module, text) in files)
            foreach (Match m in SqlDeclaration().Matches(text))
                if (!owners.TryAdd(m.Groups[1].Value, module) && owners[m.Groups[1].Value] != module) ambiguous.Add(m.Groups[1].Value);
        foreach (var name in ambiguous) owners.Remove(name);
        return owners;
    }

    private static string ModuleOf(string path)
    {
        var rest = path[(ModuleManifest.ApplicationRoot.Length + 1)..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? "" : rest[..slash];
    }

    private static string StripComments(string text) => LineComment().Replace(text, "");

    [GeneratedRegex(@"MLPlatform\.ControlPlane\.Application\.(\w+)")]
    private static partial Regex NamespaceReference();

    [GeneratedRegex(@"\[SqlQuery\(""[^""]+""\)\][^;{]*?\s(\w+Async)\s*\(", RegexOptions.Singleline)]
    private static partial Regex SqlDeclaration();

    [GeneratedRegex(@"\.(\w+Async)\s*\(")]
    private static partial Regex SqlCall();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();
}
