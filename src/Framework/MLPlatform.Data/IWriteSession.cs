using Npgsql;

namespace MLPlatform.Data;

/// <summary>Write session with a transaction managed by UnitOfWorkBehavior.</summary>
public interface IWriteSession : IReadSession
{
    ValueTask BeginAsync(CancellationToken ct);
    ValueTask CommitAsync(CancellationToken ct);
    ValueTask RollbackAsync(CancellationToken ct);
    bool InTransaction { get; }
}
