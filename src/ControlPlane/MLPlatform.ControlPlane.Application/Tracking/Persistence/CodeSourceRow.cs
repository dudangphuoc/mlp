namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A git URL prefix the server may fetch code from; the credential stays sealed.</summary>
public sealed record CodeSourceRow(Guid Id, Guid WorkspaceId, string UrlPrefix, string? SecretEnc, DateTimeOffset CreatedAt);
