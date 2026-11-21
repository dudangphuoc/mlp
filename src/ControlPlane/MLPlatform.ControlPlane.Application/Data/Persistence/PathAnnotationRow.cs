using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

public sealed record PathAnnotationRow(string Path, string Kind, int ClassId, string Geometry, string Source);
