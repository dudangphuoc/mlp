using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Monitoring;

public sealed record MonitorConfigRow(Guid DeploymentId, Guid WorkspaceId, Guid? DatasetVersionId, string LabelColumn, string? Baseline, long BaselineRows,
    double PsiWarn, double PsiAlert, int WindowSize, int MinSamples, bool Enabled, DateTimeOffset UpdatedAt);
