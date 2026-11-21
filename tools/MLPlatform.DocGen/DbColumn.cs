namespace MLPlatform.DocGen;

public sealed record DbColumn(string Name, string Type, bool Nullable, string? Default);
