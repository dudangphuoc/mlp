using Npgsql;

namespace MLPlatform.Data;

/// <summary>Two data sources: read (read-only) and write.</summary>
public sealed class DataSources(NpgsqlDataSource read, NpgsqlDataSource write)
{
    public NpgsqlDataSource Read { get; } = read;
    public NpgsqlDataSource Write { get; } = write;

    public static DataSources Create(string connectionString)
    {
        static NpgsqlDataSource Build(string cs) => new NpgsqlSlimDataSourceBuilder(cs).EnableArrays().Build();
        var rw = new NpgsqlConnectionStringBuilder(connectionString);
        if (!connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("MaxPoolSize", StringComparison.OrdinalIgnoreCase))
            rw.MaxPoolSize = 20;
        if (rw.ConnectionIdleLifetime == 300) rw.ConnectionIdleLifetime = 60;
        var ro = new NpgsqlConnectionStringBuilder(rw.ConnectionString) { Options = "-c default_transaction_read_only=on" };
        return new DataSources(Build(ro.ConnectionString), Build(rw.ConnectionString));
    }
}
