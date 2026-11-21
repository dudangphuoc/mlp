using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Application.Common;

/// <summary>Scoped queue drained by <see cref="UnitOfWorkBehavior"/> after the outermost commit; the last work queued under a key wins.</summary>
public sealed class AfterCommitQueue : IAfterCommit
{
    private readonly Dictionary<string, Func<CancellationToken, Task>> _work = new(StringComparer.Ordinal);

    public void Enqueue(string key, Func<CancellationToken, Task> work)
    {
        _work.Remove(key);
        _work[key] = work;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var pending = _work.Values.ToArray();
        _work.Clear();
        foreach (var work in pending) await work(ct);
    }

    public void Discard() => _work.Clear();
}
