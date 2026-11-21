using System.Text.RegularExpressions;

namespace MLPlatform.CodeGate.Rules;

/// <summary>MLPMIG001: migrations are db/<schema>/<yyyyMMddHHmm>_<name>.sql, unique timestamps, additive
/// only.</summary>
public static partial class MigrationRules
{
    public static IEnumerable<Finding> Check(RepoFiles repo)
    {
        var stamps = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in repo.Under("db", "*.sql"))
        {
            var name = Path.GetFileName(file);
            var location = repo.Relative(file);
            if (!MigrationName().IsMatch(name))
            {
                yield return new("MLPMIG001", location, location, "migration name must be yyyyMMddHHmm_name.sql");
                continue;
            }
            if (!stamps.TryAdd(name[..12], location))
                yield return new("MLPMIG001", location, location, $"timestamp clashes with {stamps[name[..12]]}");
            if (DropStatement().IsMatch(File.ReadAllText(file)))
                yield return new("MLPMIG001", location, location, "migrations must not DROP TABLE/COLUMN");
        }
    }

    [GeneratedRegex(@"^\d{12}_[a-z0-9_]+\.sql$")]
    private static partial Regex MigrationName();

    [GeneratedRegex(@"\bdrop\s+(table|column)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DropStatement();
}
