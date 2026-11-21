using System.Text;
using MLPlatform.CodeGate.Comments;

namespace MLPlatform.CodeGate.Reports;

/// <summary>CSV of every C# comment so their knowledge survives file moves (refactor plan step 0.5).</summary>
public static class CommentInventory
{
    public static void Write(RepoFiles repo, string path)
    {
        var csv = new StringBuilder("file,line,placement,documentation,symbol,lines,longest,spec_reference,text\n");
        foreach (var c in CommentScanner.Scan(repo))
            csv.Append($"{c.File},{c.Line},{c.Placement},{c.IsDocumentation},{c.Symbol},{c.LineCount},{c.LongestLine},{c.HasSpecReference},{Quote(c.Text)}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, csv.ToString());
    }

    private static string Quote(string text) => $"\"{text.Replace("\"", "\"\"").Replace("\r", "")}\"";
}
