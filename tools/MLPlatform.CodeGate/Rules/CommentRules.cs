using MLPlatform.CodeGate.Comments;

namespace MLPlatform.CodeGate.Rules;

/// <summary>MLPCODE001 no comment inside a body; MLPCODE003 declaration comments at most 2 lines of at most 120 characters.</summary>
public static class CommentRules
{
    private const int MaxLines = 2;
    private const int MaxLineLength = 120;

    public static IEnumerable<Finding> Check(RepoFiles repo)
    {
        foreach (var c in CommentScanner.Scan(repo))
        {
            var location = $"{c.File}:{c.Line}";
            if (c.Placement == CommentPlacement.InsideBody)
                yield return new("MLPCODE001", location, location, $"comment inside the body of {c.Symbol}");
            else if (c.LineCount > MaxLines || c.LongestLine > MaxLineLength)
                yield return new("MLPCODE003", location, location, $"comment on {c.Symbol} has {c.LineCount} lines, longest {c.LongestLine} chars");
        }
    }
}
