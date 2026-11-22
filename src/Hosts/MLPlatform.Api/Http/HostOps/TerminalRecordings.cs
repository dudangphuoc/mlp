namespace MLPlatform.Api.Http.HostOps;

/// <summary>Where terminal recordings live in the artifacts bucket; kept 30 days.</summary>
public static class TerminalRecordings
{
    public const string Prefix = "terminal-recordings/";
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public static string Key(Guid workspace, Guid session, DateTimeOffset at) => $"{Prefix}{workspace:N}/{at:yyyy-MM-dd}/{session:N}.cast.gz";
}
