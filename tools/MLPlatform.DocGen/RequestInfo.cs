namespace MLPlatform.DocGen;

public sealed record RequestInfo(string FullName, bool IsCommand, string Access, string? Permission, IReadOnlyList<string> Roles)
{
    public string ShortName => FullName[(FullName.LastIndexOf('.') + 1)..];

    public string Module => FullName.Split('.') is { Length: > 4 } parts ? parts[3] : "";
}
