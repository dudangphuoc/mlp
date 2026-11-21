using Npgsql;

namespace MLPlatform.Data;

/// <summary>Read-only session; query handlers only receive this type.</summary>
public interface IReadSession
{
    ValueTask<NpgsqlConnection> OpenAsync(CancellationToken ct);
    NpgsqlTransaction? Transaction { get; }
    /// <summary>Commits any open transaction and returns the connection to the pool; the next call reopens
    /// one.</summary>
    ValueTask ReleaseAsync(CancellationToken ct);
}
