using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

public sealed record BrowseResult(IReadOnlyList<BrowseFolderRow> Folders, IReadOnlyList<BrowsedFile> Files, long Total, string ClassesJson);
