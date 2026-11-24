namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A script of a git repository that can be started from the web.</summary>
public sealed record CodeEntryRow(Guid Id, Guid WorkspaceId, Guid ProjectId, string Name, string GitRemote, string Ref, string Script, string Machine,
    int Gpus, string Image, DateTimeOffset CreatedAt);
