using System.Text.RegularExpressions;

namespace MLPlatform.CodeGate.Rules;

/// <summary>Tables that carry a workspace_id column, read from the migrations.</summary>
public static partial class WorkspaceTables
{
    public static HashSet<string> Load(RepoFiles repo)
    {
        HashSet<string> tables = new(StringComparer.OrdinalIgnoreCase);
        foreach (var file in repo.Under("db", "*.sql"))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in CreateTable().Matches(text))
                if (WorkspaceColumn().IsMatch(m.Groups[2].Value)) tables.Add(m.Groups[1].Value);
            foreach (Match m in AddWorkspaceColumn().Matches(text)) tables.Add(m.Groups[1].Value);
        }
        return tables;
    }

    [GeneratedRegex(@"create\s+table\s+(?:if\s+not\s+exists\s+)?([a-z_]+\.[a-z_0-9]+)\s*\((.*?)\n\);", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CreateTable();

    [GeneratedRegex(@"^\s*workspace_id\s", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex WorkspaceColumn();

    [GeneratedRegex(@"alter\s+table\s+(?:if\s+exists\s+)?([a-z_]+\.[a-z_0-9]+)\s+add\s+column\s+(?:if\s+not\s+exists\s+)?workspace_id\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddWorkspaceColumn();
}
