using Npgsql;

namespace MLPlatform.Data;

/// <summary>Marks a partial method generated from a .sql file (20/02 §5.2). Path relative to the Sql/ folder.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SqlQueryAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}
