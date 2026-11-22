namespace MLPlatform.Agent.Terminal;

/// <summary>A pseudo terminal running one shell.</summary>
public interface IPty : IDisposable
{
    Stream Output { get; }
    Stream Input { get; }
    void Resize(int cols, int rows);
    Task<int> WaitForExitAsync(CancellationToken ct);
    void Kill();
}
