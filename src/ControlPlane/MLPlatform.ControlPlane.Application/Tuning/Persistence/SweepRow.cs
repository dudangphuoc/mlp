using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tuning;

public sealed record SweepRow(Guid Id, Guid WorkspaceId, Guid ProjectId, string Name, string Recipe, string Variant, string Task, string Algorithm,
    string Space, string BaseParams, string Objective, string Direction, int MaxTrials, int Parallel, int Seed, double Cpus, int Gpus,
    Guid? DatasetVersionId, string? ImageOverride, string? Network, string State, string? StateReason, int? BestTrial, double? BestValue,
    Guid CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);
