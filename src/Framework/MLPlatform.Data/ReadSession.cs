using Npgsql;

namespace MLPlatform.Data;

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
