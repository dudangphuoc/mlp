using Npgsql;

namespace MLPlatform.Data;

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
