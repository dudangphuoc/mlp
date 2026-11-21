using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MLPlatform.Refactor;

/// <summary>Removes comments inside member bodies and shortens declaration comments to one sentence of at most two
/// lines.</summary>
public static partial class CommentCleaner
{
    private const int MaxLineLength = 120;

    public static int Run(IEnumerable<string> files)
    {
        var changed = 0;
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var root = CSharpSyntaxTree.ParseText(text, SourceFiles.ParseOptions).GetRoot();
            var rewritten = new Rewriter().Visit(root)!.ToFullString();
            rewritten = BlankLineRuns().Replace(rewritten, "\n\n");
            if (rewritten == text) continue;
            File.WriteAllText(file, rewritten);
            changed++;
        }
        Console.WriteLine($"{changed} file(s) changed");
        return 0;
    }

    private sealed class Rewriter : CSharpSyntaxRewriter
    {
        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            var insideBody = token.Parent?.AncestorsAndSelf().Any(IsBody) == true && !LeadsDeclaration(token);
            var leading = Clean(token.LeadingTrivia, insideBody, leading: true);
            var trailing = Clean(token.TrailingTrivia, insideBody, leading: false);
            return token.WithLeadingTrivia(leading).WithTrailingTrivia(trailing);
        }

        private static SyntaxTriviaList Clean(SyntaxTriviaList list, bool insideBody, bool leading)
        {
            var result = new List<SyntaxTrivia>();
            for (var i = 0; i < list.Count; i++)
            {
                var trivia = list[i];
                if (IsPlainComment(trivia) && (insideBody || !leading))
                {
                    DropIndentBefore(result);
                    if (leading && i + 1 < list.Count && list[i + 1].IsKind(SyntaxKind.EndOfLineTrivia)) i++;
                    continue;
                }
                if (IsPlainComment(trivia) || IsDocComment(trivia))
                {
                    var indent = result.Count > 0 && result[^1].IsKind(SyntaxKind.WhitespaceTrivia) ? result[^1].ToFullString() : "";
                    result.Add(Shorten(trivia, indent));
                    continue;
                }
                result.Add(trivia);
            }
            return SyntaxFactory.TriviaList(result);
        }

        private static void DropIndentBefore(List<SyntaxTrivia> result)
        {
            while (result.Count > 0 && result[^1].IsKind(SyntaxKind.WhitespaceTrivia)) result.RemoveAt(result.Count - 1);
        }
    }

    private static bool IsPlainComment(SyntaxTrivia t) => t.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia;

    private static bool IsDocComment(SyntaxTrivia t) => t.Kind() is SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia;

    private static bool IsBody(SyntaxNode node) => node switch
    {
        BlockSyntax { Parent: BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax } => true,
        ArrowExpressionClauseSyntax or AnonymousFunctionExpressionSyntax or GlobalStatementSyntax => true,
        _ => false,
    };

    private static bool LeadsDeclaration(SyntaxToken token) =>
        token.Parent?.AncestorsAndSelf().FirstOrDefault(n => n is MemberDeclarationSyntax or LocalFunctionStatementSyntax) is { } declaration
        && declaration is not GlobalStatementSyntax
        && declaration.GetFirstToken() == token;

    private static SyntaxTrivia Shorten(SyntaxTrivia trivia, string indent)
    {
        var full = trivia.ToFullString();
        var lines = full.Split('\n');
        if (lines.Count(l => l.Trim().Length > 0) <= 2 && lines.All(l => l.Trim().Length <= MaxLineLength)) return trivia;
        var isDoc = IsDocComment(trivia);
        var sentence = FirstSentence(Plain(full));
        var prefix = isDoc ? "/// " : "// ";
        var body = isDoc ? $"<summary>{sentence}</summary>" : sentence;
        var wrapped = Wrap(body, MaxLineLength - prefix.Length - indent.Length).Take(2).Select((l, i) => (i == 0 ? "" : indent) + prefix + l);
        var text = string.Join("\n", wrapped) + "\n";
        return isDoc
            ? SyntaxFactory.ParseLeadingTrivia(text).First(IsDocComment)
            : SyntaxFactory.Comment(text.TrimEnd('\n').TrimStart());
    }

    private static string Plain(string comment)
    {
        var text = DocPrefix().Replace(comment, " ");
        text = CrefOrSee().Replace(text, m => m.Groups[1].Value.Split('.').Last());
        text = XmlTag().Replace(text, " ");
        return Spaces().Replace(System.Net.WebUtility.HtmlDecode(text), " ").Trim();
    }

    private static string FirstSentence(string text)
    {
        var end = SentenceEnd().Match(text);
        var sentence = end.Success ? text[..(end.Index + 1)] : text;
        var limit = 2 * (MaxLineLength - 4) - 20;
        if (sentence.Length <= limit) return sentence;
        var cut = sentence.LastIndexOfAny([';', ':', ','], limit);
        return (cut > 40 ? sentence[..cut] : sentence[..sentence.LastIndexOf(' ', limit)]).TrimEnd() + ".";
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width) { yield return line.ToString(); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }

    [GeneratedRegex(@"(^|\n)\s*(///|//|/\*+|\*+/|\*)")]
    private static partial Regex DocPrefix();

    [GeneratedRegex(@"<(?:see|seealso|paramref|typeparamref)\s+(?:cref|name|langword)=""([^""]+)""\s*/>")]
    private static partial Regex CrefOrSee();

    [GeneratedRegex(@"</?[a-zA-Z][^>]*>")]
    private static partial Regex XmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[.!?](\s|$)")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\n[ \t]*\n([ \t]*\n)+")]
    private static partial Regex BlankLineRuns();
}
