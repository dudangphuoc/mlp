using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MLPlatform.CodeGate.Comments;

/// <summary>Finds every comment in hand-written C# and decides whether it sits inside a body or on a
/// declaration.</summary>
public static class CommentScanner
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    public static IEnumerable<CommentInfo> Scan(RepoFiles repo) => repo.HandWrittenCSharp().SelectMany(f => Scan(repo, f));

    private static IEnumerable<CommentInfo> Scan(RepoFiles repo, string file)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), ParseOptions, file);
        var root = tree.GetRoot();
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: false).Where(IsComment))
        {
            var text = trivia.ToFullString().TrimEnd();
            var lines = text.Split('\n');
            yield return new CommentInfo(
                repo.Relative(file),
                trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                PlacementOf(trivia),
                trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia),
                SymbolOf(trivia.Token.Parent),
                lines.Length,
                lines.Max(l => l.Trim().Length),
                text);
        }
    }

    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
            or SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia;

    private static CommentPlacement PlacementOf(SyntaxTrivia trivia)
    {
        var node = trivia.Token.Parent;
        if (node is null) return CommentPlacement.Elsewhere;
        if (node.AncestorsAndSelf().Any(IsBodyNode) && !IsLeadingTriviaOfDeclaration(trivia)) return CommentPlacement.InsideBody;
        return IsLeadingTriviaOfDeclaration(trivia) || node.AncestorsAndSelf().Any(n => n is MemberDeclarationSyntax or ParameterSyntax or EnumMemberDeclarationSyntax)
            ? CommentPlacement.OnDeclaration
            : CommentPlacement.Elsewhere;
    }

    private static bool IsBodyNode(SyntaxNode node) => node switch
    {
        BlockSyntax { Parent: BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax } => true,
        ArrowExpressionClauseSyntax or AnonymousFunctionExpressionSyntax or GlobalStatementSyntax => true,
        _ => false,
    };

    private static bool IsLeadingTriviaOfDeclaration(SyntaxTrivia trivia) =>
        trivia.Token.LeadingTrivia.Contains(trivia)
        && trivia.Token.Parent is { } parent
        && parent.AncestorsAndSelf().FirstOrDefault(n => n is MemberDeclarationSyntax or LocalFunctionStatementSyntax) is { } declaration
        && declaration.GetFirstToken() == trivia.Token
        && declaration is not GlobalStatementSyntax;

    private static string SymbolOf(SyntaxNode? node)
    {
        var names = node?.AncestorsAndSelf().Select(NameOf).Where(n => n.Length > 0).Reverse().ToList() ?? [];
        return names.Count == 0 ? "" : string.Join('.', names);
    }

    private static string NameOf(SyntaxNode node) => node switch
    {
        BaseNamespaceDeclarationSyntax ns => ns.Name.ToString(),
        BaseTypeDeclarationSyntax type => type.Identifier.Text,
        DelegateDeclarationSyntax d => d.Identifier.Text,
        MethodDeclarationSyntax m => m.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text + ".ctor",
        PropertyDeclarationSyntax p => p.Identifier.Text,
        LocalFunctionStatementSyntax f => f.Identifier.Text,
        _ => "",
    };
}
