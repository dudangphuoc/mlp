using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MLPlatform.DocGen;

/// <summary>Which commands and queries each gRPC method sends, read from the service classes under Api/Grpc.</summary>
public static partial class RpcBindings
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Scan(string root)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src/Hosts/MLPlatform.Api/Grpc"), "*.cs", SearchOption.AllDirectories))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), new CSharpParseOptions(LanguageVersion.Preview));
            foreach (var type in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (ServiceOf(type) is not { } service) continue;
                foreach (var method in type.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Modifiers.Any(SyntaxKind.OverrideKeyword)))
                    result[$"{service}/{method.Identifier.Text}"] = [.. RequestCreation().Matches(method.ToString()).Select(m => m.Groups[1].Value).Distinct()];
            }
        }
        return result;
    }

    private static string? ServiceOf(ClassDeclarationSyntax type) =>
        type.BaseList?.Types.Select(t => t.Type.ToString()).FirstOrDefault(t => t.EndsWith("ServiceBase", StringComparison.Ordinal)) is { } baseType
            ? baseType.Split('.')[0]
            : null;

    [GeneratedRegex(@"new\s+(?:[\w.]+\.)?(\w+(?:Command|Query))\s*\(")]
    private static partial Regex RequestCreation();
}
