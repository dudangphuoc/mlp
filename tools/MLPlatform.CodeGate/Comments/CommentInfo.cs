namespace MLPlatform.CodeGate.Comments;

public sealed record CommentInfo(string File, int Line, CommentPlacement Placement, bool IsDocumentation, string Symbol, int LineCount, int LongestLine, string Text)
{
    public bool HasSpecReference => SpecReferences.IsMatch(Text);

    private static readonly System.Text.RegularExpressions.Regex SpecReferences =
        new(@"\bD-\d+\b|\bM\d{2}\s?§|\d{2}/\d{2}\s§|\bP\d-\d+\b", System.Text.RegularExpressions.RegexOptions.Compiled);
}
