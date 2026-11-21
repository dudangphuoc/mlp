namespace MLPlatform.CodeGate;

/// <summary>One gate violation; Key is path-independent so moving files does not invalidate the baseline.</summary>
public sealed record Finding(string Rule, string Key, string Location, string Message)
{
    public string BaselineEntry => $"{Rule} {Key}";
}
