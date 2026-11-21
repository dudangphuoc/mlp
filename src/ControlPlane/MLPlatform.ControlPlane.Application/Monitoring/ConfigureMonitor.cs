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

/// <summary>Sets the baseline = a tabular dataset version (reads the version's CSV from S3 storage, ≤ 50 MB) + PSI
/// thresholds.</summary>
[RequiresPermission("deployment.manage")]
public sealed record ConfigureMonitorCommand(Guid WorkspaceId, Guid DeploymentId, Guid DatasetVersionId, string LabelColumn, double PsiWarn, double PsiAlert,
    int WindowSize, int MinSamples, bool Enabled) : ICommand<MonitorConfigRow>;

public sealed class ConfigureMonitorCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, S3Client s3) : ICommandHandler<ConfigureMonitorCommand, MonitorConfigRow>
{
    public async ValueTask<MonitorConfigRow> Handle(ConfigureMonitorCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        _ = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        var v = await db.GetVersionAsync(c.DatasetVersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        string[] header = [];
        var rows = new List<string[]>();
        long total = 0;
        foreach (var f in (await db.ListVersionFilesAsync(v.Id, ct)).Where(f => f.Path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
        {
            total += f.SizeBytes;
            if (total > 50L * 1024 * 1024) throw new ResourceExhaustedException("MONITOR_BASELINE_TOO_LARGE", "Baseline CSV > 50 MB (sampling comes later)");
            var bytes = await s3.GetAsync(Buckets.Datasets, v.Prefix + f.Path, ct) ?? [];
            var (h, r) = Drift.ParseCsv(Encoding.UTF8.GetString(bytes));
            if (header.Length == 0) header = h;
            if (h.SequenceEqual(header)) rows.AddRange(r);
        }
        if (header.Length == 0 || rows.Count == 0)
            throw new PreconditionException("MONITOR_BASELINE_UNSUPPORTED", "Dataset version has no tabular CSV to use as a baseline");
        var label = string.IsNullOrWhiteSpace(c.LabelColumn) ? "label" : c.LabelColumn.Trim();
        var baseline = Drift.BuildBaseline(header, rows, label);
        var warn = c.PsiWarn > 0 ? c.PsiWarn : 0.1;
        var alert = c.PsiAlert > warn ? c.PsiAlert : Math.Max(0.25, warn * 2);
        await db.UpsertConfigAsync(c.DeploymentId, c.WorkspaceId, v.Id, label, baseline.ToJsonString(), rows.Count, warn, alert,
            c.WindowSize is >= 20 and <= 100_000 ? c.WindowSize : 500, c.MinSamples is >= 1 and <= 10_000 ? c.MinSamples : 20, c.Enabled, uid, clock.UtcNow, ct);
        return (await db.GetConfigAsync(c.DeploymentId, ct))!;
    }
}
