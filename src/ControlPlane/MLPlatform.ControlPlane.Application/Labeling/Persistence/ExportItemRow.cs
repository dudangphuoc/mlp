using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Path of an item whose labels go into an exported version.</summary>
public sealed record ExportItemRow(string Path);
