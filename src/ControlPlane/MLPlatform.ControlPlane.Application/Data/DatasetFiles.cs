namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>A dataset version's files with download links.</summary>
public sealed record DatasetFiles(Guid VersionId, string Name, int Version, IReadOnlyList<VersionFileLink> Files);
