namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A model reference pinned to a commit, with where to fetch each file (link, or the bytes for small files of
/// private repos whose host gives no signed link).</summary>
public sealed record ModelFiles(string Repo, string Revision, IReadOnlyList<ModelFile> Files);
