using System.Text.RegularExpressions;

namespace MLPlatform.DocGen;

/// <summary>Tables and columns replayed from db/ migrations in order, and the queries under Sql/ that read or write
/// each table.</summary>
public static partial class DbSchema
{
    public static IReadOnlyList<DbTable> Load(string root)
    {
        var tables = new Dictionary<string, DbTable>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "db"), "*.sql", SearchOption.AllDirectories).OrderBy(Path.GetFileName, StringComparer.Ordinal))
            Apply(tables, StripComments(File.ReadAllText(file)));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src/ControlPlane/MLPlatform.ControlPlane.Application/Sql"), "*.sql", SearchOption.AllDirectories))
            RecordUsage(tables, root, file);
        return [.. tables.Values.OrderBy(t => t.FullName, StringComparer.Ordinal)];
    }

    private static void Apply(Dictionary<string, DbTable> tables, string sql)
    {
        foreach (Match m in CreateTable().Matches(sql))
        {
            var parts = m.Groups[1].Value.Split('.');
            tables[m.Groups[1].Value] = new DbTable(parts[0], parts[1], [.. Columns(m.Groups[2].Value)], [], []);
        }
        foreach (Match m in AddColumn().Matches(sql))
            if (tables.TryGetValue(m.Groups[1].Value, out var table) && Column(m.Groups[2].Value) is { } column && table.Columns.All(c => c.Name != column.Name))
                table.Columns.Add(column);
        foreach (Match m in DropColumn().Matches(sql))
            if (tables.TryGetValue(m.Groups[1].Value, out var table)) table.Columns.RemoveAll(c => c.Name == m.Groups[2].Value);
    }

    private static IEnumerable<DbColumn> Columns(string body) =>
        SplitTopLevel(body).Select(Column).OfType<DbColumn>();

    private static DbColumn? Column(string definition)
    {
        var text = Spaces().Replace(definition.Trim(), " ");
        if (text.Length == 0 || ConstraintStart().IsMatch(text)) return null;
        var name = text.Split(' ')[0].Trim('"');
        var rest = text[name.Length..].Trim();
        var type = TypeEnd().Split(rest)[0].Trim();
        var defaultValue = DefaultValue().Match(rest) is { Success: true } d ? d.Groups[1].Value.Trim() : null;
        var nullable = !rest.Contains("not null", StringComparison.OrdinalIgnoreCase) && !rest.Contains("primary key", StringComparison.OrdinalIgnoreCase);
        return new DbColumn(name, type, nullable, defaultValue);
    }

    private static IEnumerable<string> SplitTopLevel(string body)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '(') depth++;
            else if (body[i] == ')') depth--;
            else if (body[i] == ',' && depth == 0) { yield return body[start..i]; start = i + 1; }
        }
        yield return body[start..];
    }

    private static void RecordUsage(Dictionary<string, DbTable> tables, string root, string file)
    {
        var sql = StripComments(File.ReadAllText(file));
        var query = Path.GetRelativePath(Path.Combine(root, "src/ControlPlane/MLPlatform.ControlPlane.Application/Sql"), file).Replace('\\', '/').Replace(".sql", "");
        foreach (Match m in Write().Matches(sql))
            if (tables.TryGetValue(m.Groups[1].Value, out var t) && !t.WrittenBy.Contains(query)) t.WrittenBy.Add(query);
        foreach (Match m in Read().Matches(sql))
            if (tables.TryGetValue(m.Groups[1].Value, out var t) && !t.ReadBy.Contains(query)) t.ReadBy.Add(query);
    }

    private static string StripComments(string sql) => LineComment().Replace(sql, "");

    [GeneratedRegex(@"create\s+table\s+(?:if\s+not\s+exists\s+)?([a-z_]+\.[a-z_0-9]+)\s*\((.*?)\n\s*\)\s*;", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CreateTable();

    [GeneratedRegex(@"alter\s+table\s+(?:if\s+exists\s+)?([a-z_]+\.[a-z_0-9]+)\s+add\s+column\s+(?:if\s+not\s+exists\s+)?([^,;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AddColumn();

    [GeneratedRegex(@"alter\s+table\s+(?:if\s+exists\s+)?([a-z_]+\.[a-z_0-9]+)\s+drop\s+column\s+(?:if\s+exists\s+)?([a-z_0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DropColumn();

    [GeneratedRegex(@"^(primary\s+key|unique|check|foreign\s+key|constraint|exclude)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ConstraintStart();

    [GeneratedRegex(@"\s(?=not\s+null|null|default|primary|references|unique|check|generated|constraint)", RegexOptions.IgnoreCase)]
    private static partial Regex TypeEnd();

    [GeneratedRegex(@"default\s+(.+?)(?=\s+(?:not\s+null|null|references|unique|check|primary)|$)", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultValue();

    [GeneratedRegex(@"\b(?:insert\s+into|update|delete\s+from)\s+([a-z_]+\.[a-z_0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Write();

    [GeneratedRegex(@"\b(?:from|join)\s+([a-z_]+\.[a-z_0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Read();

    [GeneratedRegex(@"--[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
