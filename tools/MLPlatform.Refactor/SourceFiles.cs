using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MLPlatform.Refactor;

public static class SourceFiles
{
    public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    public static IEnumerable<MemberDeclarationSyntax> TopLevelTypes(CompilationUnitSyntax root) =>
        root.Members.SelectMany(m => m is BaseNamespaceDeclarationSyntax ns ? ns.Members : [m])
            .Where(m => m is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);

    public static string NameOf(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax t => t.Identifier.Text,
        DelegateDeclarationSyntax d => d.Identifier.Text,
        _ => "",
    };

    public static string? NamespaceOf(CompilationUnitSyntax root) =>
        root.Members.OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();

    public static string Header(CompilationUnitSyntax root)
    {
        var usings = string.Concat(root.Usings.Select(u => u.ToFullString()));
        var externs = string.Concat(root.Externs.Select(e => e.ToFullString()));
        return externs + usings;
    }
}
