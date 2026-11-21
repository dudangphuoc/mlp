using System.Text.RegularExpressions;

namespace MLPlatform.CodeGate.Rules;

/// <summary>MLPSQL001 query header, MLPSQL010 workspace filter, MLPSQL020 Sql/<folder> stays in its schema.</summary>
public static partial class SqlRules
{
    private static readonly HashSet<string> SharedSchemas = ["platform"];

    public static IEnumerable<Finding> Check(RepoFiles repo)
    {
        var workspaceTables = WorkspaceTables.Load(repo);
        foreach (var file in repo.Under("src", "*.sql").Where(f => f.Contains("/Sql/", StringComparison.Ordinal)))
        {
            var location = repo.Relative(file);
            var folder = location[(location.IndexOf("/Sql/", StringComparison.Ordinal) + 5)..].Split('/')[0];
            var text = File.ReadAllText(file);
            var heads = QueryHeader().Matches(text);
            if (heads.Count == 0 || !text.TrimStart().StartsWith("--", StringComparison.Ordinal))
            {
                yield return new("MLPSQL001", $"{folder}/{Path.GetFileNameWithoutExtension(file)}", location, "missing -- mlp:query header");
                continue;
            }
            for (var i = 0; i < heads.Count; i++)
                foreach (var finding in CheckQuery(text, heads, i, folder, location, workspaceTables))
                    yield return finding;
        }
    }

    private static IEnumerable<Finding> CheckQuery(string text, MatchCollection heads, int index, string folder, string location, HashSet<string> workspaceTables)
    {
        var (name, kind) = (heads[index].Groups[1].Value, heads[index].Groups[2].Value);
        var key = $"{folder}/{name}";
        if (kind is not ("one" or "many" or "exec" or "scalar" or "copy"))
            yield return new("MLPSQL001", key, location, $"invalid kind '{kind}'");
        if (!PascalCase().IsMatch(name))
            yield return new("MLPSQL001", key, location, "query name must be PascalCase");

        var end = index + 1 < heads.Count ? heads[index + 1].Index : text.Length;
        var raw = text[(heads[index].Index + heads[index].Length)..end];
        var body = LineComment().Replace(raw, "");
        var references = TableReference().Matches(body)
            .Select(m => (Schema: m.Groups[1].Value.ToLowerInvariant(), Table: $"{m.Groups[1].Value}.{m.Groups[2].Value}".ToLowerInvariant()))
            .ToList();

        foreach (var schema in references.Select(r => r.Schema).Where(s => s != SchemaOf(folder) && !SharedSchemas.Contains(s)).Distinct())
            yield return new("MLPSQL020", $"{key}->{schema}", location, $"folder {folder} touches schema {schema}");

        var exempt = WorkspaceExemption().IsMatch(raw);
        if (!exempt && references.Any(r => workspaceTables.Contains(r.Table)) && !body.Contains("workspace_id", StringComparison.OrdinalIgnoreCase))
            yield return new("MLPSQL010", key, location, $"{name}: table has workspace_id but the query does not filter on it");
    }

    private static string SchemaOf(string folder) => folder.ToLowerInvariant() switch { "llm" => "llmops", var x => x };

    [GeneratedRegex(@"^--\s*mlp:query\s+(\S+)\s+:(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex QueryHeader();

    [GeneratedRegex(@"\b(?:from|join|into|update|exists\s*\(\s*select\s+1\s+from)\s+([a-z_]+)\.([a-z_0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex TableReference();

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex PascalCase();

    [GeneratedRegex("--[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"--\s*mlp:no-workspace\s+\S")]
    private static partial Regex WorkspaceExemption();
}
