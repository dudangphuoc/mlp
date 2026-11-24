namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Resolves hf://owner/name[@revision][#file=path] with the workspace's Hugging Face token, which never leaves
/// the api.</summary>
public interface IModelRefResolver
{
    Task<ModelFiles> ResolveAsync(Guid workspaceId, string reference, CancellationToken ct);
}
