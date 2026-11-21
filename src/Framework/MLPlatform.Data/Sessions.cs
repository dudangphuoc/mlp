using Npgsql;

namespace MLPlatform.Data;

/// <summary>Marks a partial method generated from a .sql file (20/02 §5.2). Path relative to the Sql/ folder.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SqlQueryAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}

/// <summary>Read-only session; query handlers only receive this type.</summary>
public interface IReadSession
{
    ValueTask<NpgsqlConnection> OpenAsync(CancellationToken ct);
    NpgsqlTransaction? Transaction { get; }
    /// <summary>Commits any open transaction and returns the connection to the pool; the next call reopens one.
    /// Use before slow external calls (model inference, HTTP) so a waiting request does not hold a pooled connection.</summary>
    ValueTask ReleaseAsync(CancellationToken ct);
}

/// <summary>Write session with a transaction managed by UnitOfWorkBehavior.</summary>
public interface IWriteSession : IReadSession
{
    ValueTask BeginAsync(CancellationToken ct);
    ValueTask CommitAsync(CancellationToken ct);
    ValueTask RollbackAsync(CancellationToken ct);
    bool InTransaction { get; }
}

/// <summary>Two data sources: read (read-only) and write.</summary>
public sealed class DataSources(NpgsqlDataSource read, NpgsqlDataSource write)
{
    public NpgsqlDataSource Read { get; } = read;
    public NpgsqlDataSource Write { get; } = write;

    public static DataSources Create(string connectionString)
    {
        static NpgsqlDataSource Build(string cs) => new NpgsqlSlimDataSourceBuilder(cs).EnableArrays().Build();
        // The database server is shared (max_connections is small); two default pools of 100 each can exhaust it.
        // Cap each pool unless the connection string sets its own limit; excess requests queue instead of failing.
        var rw = new NpgsqlConnectionStringBuilder(connectionString);
        if (!connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("MaxPoolSize", StringComparison.OrdinalIgnoreCase))
            rw.MaxPoolSize = 20;
        if (rw.ConnectionIdleLifetime == 300) rw.ConnectionIdleLifetime = 60;
        var ro = new NpgsqlConnectionStringBuilder(rw.ConnectionString) { Options = "-c default_transaction_read_only=on" };
        return new DataSources(Build(ro.ConnectionString), Build(rw.ConnectionString));
    }
}

public sealed class ReadSession(DataSources sources) : IReadSession, IAsyncDisposable
{
    private NpgsqlConnection? _conn;
    public NpgsqlTransaction? Transaction => null;

    public async ValueTask<NpgsqlConnection> OpenAsync(CancellationToken ct) =>
        _conn ??= await sources.Read.OpenConnectionAsync(ct);

    public async ValueTask ReleaseAsync(CancellationToken ct)
    {
        if (_conn is null) return;
        await _conn.DisposeAsync();
        _conn = null;
    }

    public async ValueTask DisposeAsync() => await ReleaseAsync(default);
}

public sealed class WriteSession(DataSources sources) : IWriteSession, IAsyncDisposable
{
    private NpgsqlConnection? _conn;
    public NpgsqlTransaction? Transaction { get; private set; }
    public bool InTransaction => Transaction is not null || _reopenTransaction;

    public async ValueTask<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        if (_conn is not null) return _conn;
        _conn = await sources.Write.OpenConnectionAsync(ct);
        if (_reopenTransaction) { _reopenTransaction = false; Transaction = await _conn.BeginTransactionAsync(ct); }
        return _conn;
    }

    public async ValueTask BeginAsync(CancellationToken ct)
    {
        var c = await OpenAsync(ct);
        Transaction ??= await c.BeginTransactionAsync(ct);
    }

    public async ValueTask CommitAsync(CancellationToken ct)
    {
        _reopenTransaction = false;
        if (Transaction is null) return;
        await Transaction.CommitAsync(ct);
        await Transaction.DisposeAsync();
        Transaction = null;
    }

    public async ValueTask RollbackAsync(CancellationToken ct)
    {
        _reopenTransaction = false;
        if (Transaction is null) return;
        await Transaction.RollbackAsync(ct);
        await Transaction.DisposeAsync();
        Transaction = null;
    }

    public async ValueTask ReleaseAsync(CancellationToken ct)
    {
        var reopen = Transaction is not null;
        await CommitAsync(ct);
        if (_conn is not null) { await _conn.DisposeAsync(); _conn = null; }
        _reopenTransaction = reopen;
    }

    private bool _reopenTransaction;

    public async ValueTask DisposeAsync()
    {
        if (Transaction is not null) await Transaction.DisposeAsync();
        if (_conn is not null) await _conn.DisposeAsync();
    }
}
