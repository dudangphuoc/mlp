using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

// AI label proposals (Hermes phase 3, M05 §4.2).

public sealed record AssistSettingsRow(Guid ProjectId, string SourcesJson, string ClassMappingJson, bool AllowExternal, double MinConfidence, DateTimeOffset UpdatedAt);
