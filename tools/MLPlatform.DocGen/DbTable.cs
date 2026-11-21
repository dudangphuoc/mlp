namespace MLPlatform.DocGen;

public sealed record DbTable(string Schema, string Name, List<DbColumn> Columns, List<string> ReadBy, List<string> WrittenBy)
{
    public string FullName => $"{Schema}.{Name}";
}
