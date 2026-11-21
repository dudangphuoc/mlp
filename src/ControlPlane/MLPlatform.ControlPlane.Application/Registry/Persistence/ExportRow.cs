using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>One conversion of a model version to another format: running , ready (file in storage) or failed
/// .</summary>
public sealed record ExportRow(Guid Id, Guid WorkspaceId, Guid ModelVersionId, string Format, string State, string Error, Guid? JobId,
    string FileName, long SizeBytes, DateTimeOffset CreatedAt);
