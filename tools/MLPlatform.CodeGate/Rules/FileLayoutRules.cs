using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MLPlatform.CodeGate.Rules;

/// <summary>MLPCODE002: one top-level type per file, or one feature slice (a request and its handler).</summary>
public static class FileLayoutRules
{
    public static IEnumerable<Finding> Check(RepoFiles repo)
    {
        foreach (var file in repo.HandWrittenCSharp())
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file), new CSharpParseOptions(LanguageVersion.Preview)).GetCompilationUnitRoot();
            var names = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                .Where(t => t.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax)
                .Select(t => t.Identifier.Text).Distinct().ToList();
            if (names.Count > 1 && !IsSlice(names))
            {
                var path = repo.Relative(file);
                yield return new("MLPCODE002", path, path, $"{names.Count} top-level types: {string.Join(", ", names)}");
            }
        }
    }

    private static bool IsSlice(List<string> names) =>
        names.Count == 2 && names.Any(n => names.Contains(n + "Handler"));
}
