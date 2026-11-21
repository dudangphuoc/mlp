namespace MLPlatform.Cqrs;

/// <summary>Request info flowing through the pipeline (no reflection: the generator fills it in).</summary>
public sealed record RequestContext(object Request, string Name, bool IsCommand, string? Permission, Guid? WorkspaceId = null);
