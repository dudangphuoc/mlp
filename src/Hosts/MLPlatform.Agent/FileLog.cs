using Microsoft.Extensions.Logging;

namespace MLPlatform.Agent;

/// <summary>Logs to a file in the agent folder (a Windows Scheduled Task has no console).</summary>
public sealed class FileLoggerProvider(string path) : ILoggerProvider
{
    private readonly Lock _gate = new();

    public ILogger CreateLogger(string category) => new FileLogger(this, category);
    public void Dispose() { }

    internal void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 10 * 1024 * 1024) File.Move(path, path + ".1", overwrite: true);
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var w = new StreamWriter(fs);
                w.WriteLine(line);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class FileLogger(FileLoggerProvider p, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? e, Func<TState, Exception?, string> fmt)
        {
            if (!IsEnabled(level)) return;
            var cat = category[(category.LastIndexOf('.') + 1)..];
            p.Write($"{TimeProvider.System.GetUtcNow():yyyy-MM-ddTHH:mm:ssZ} {level.ToString()[..4].ToUpperInvariant()} {cat}: {fmt(state, e)}{(e is null ? "" : " | " + e.Message)}");
        }
    }
}
