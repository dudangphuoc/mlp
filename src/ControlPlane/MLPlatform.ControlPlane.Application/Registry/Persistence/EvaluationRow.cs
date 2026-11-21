using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

public sealed record EvaluationRow(Guid Id, Guid WorkspaceId, Guid ModelVersionId, Guid DatasetVersionId, string PathFilter, int MaxSamples,
    double ConfThreshold, Guid? DeploymentId, string State, string? StateReason, int Total, int Done, int Errors, string Metrics, string PerClass,
    string? Confusion, Guid CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);
