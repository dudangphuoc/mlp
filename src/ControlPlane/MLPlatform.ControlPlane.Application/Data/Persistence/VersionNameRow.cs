using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Dataset name and version number of a dataset version, for modules that only hold its id.</summary>
public sealed record VersionNameRow(Guid Id, Guid DatasetId, string DatasetName, int Number);
