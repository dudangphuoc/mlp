namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>One file of a resolved model: a download link, or its content when no link can be shared.</summary>
public sealed record ModelFile(string Path, long Size, string Url, byte[]? Content);
