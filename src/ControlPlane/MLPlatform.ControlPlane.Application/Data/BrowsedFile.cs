using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

public sealed record BrowsedFile(string Path, long SizeBytes, string Kind, string Url, string? LabelUrl, string? LabelPath, string? AnnotationsJson);
