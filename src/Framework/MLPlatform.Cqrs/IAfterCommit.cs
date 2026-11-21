namespace MLPlatform.Cqrs;

/// <summary>Work that must run only after the outermost command's transaction commits (calls to machines, containers,
/// other systems).</summary>
public interface IAfterCommit
{
    void Enqueue(string key, Func<CancellationToken, Task> work);
}
