using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Features;

public sealed record ViewRow(Guid Id, Guid WorkspaceId, string Name, int Version, Guid EntityId, string EntityName, string JoinKey, Guid SourceVersionId,
    string TimestampColumn, string Features, int TtlHours, bool Online, int? MaterializeEveryMin, string State, string? StateReason, long Rows, long Entities,
    DateTimeOffset? LastMaterializedAt, DateTimeOffset? MaxEventTs, DateTimeOffset CreatedAt);
