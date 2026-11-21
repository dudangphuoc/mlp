using Npgsql;

namespace MLPlatform.Data;

/// <summary>Read-only session; query handlers only receive this type.</summary>
public interface IReadSession
{
    ValueTask<NpgsqlConnection> OpenAsync(CancellationToken ct);
    NpgsqlTransaction? Transaction { get; }
    /// <summary>Commits any open transaction and returns the connection to the pool; the next call reopens one.
    /// Use before slow external calls (model inference, HTTP) so a waiting request does not hold a pooled connection.</summary>
    ValueTask ReleaseAsync(CancellationToken ct);
}
