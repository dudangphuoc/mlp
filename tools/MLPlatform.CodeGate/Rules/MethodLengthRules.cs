using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MLPlatform.CodeGate.Rules;

/// <summary>MLPCODE004: methods longer than 40 lines (soft limit, reported).</summary>
public static class MethodLengthRules
{
    private const int MaxLines = 40;

    public static IEnumerable<Finding> Check(RepoFiles repo)
    {
        foreach (var file in repo.HandWrittenCSharp())
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file), new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
            foreach (var member in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                var span = member.GetLocation().GetLineSpan();
                var lines = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
                if (lines <= MaxLines) continue;
                var name = member is MethodDeclarationSyntax m ? m.Identifier.Text : member.Kind().ToString();
                var path = repo.Relative(file);
                yield return new("MLPCODE004", $"{path}:{name}", $"{path}:{span.StartLinePosition.Line + 1}", $"{name} has {lines} lines");
            }
        }
    }
}
