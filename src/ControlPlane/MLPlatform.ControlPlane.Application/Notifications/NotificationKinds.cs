using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

/// <summary>P1 notification kind catalog (M16 §4) — groups shown in S-16-2; `Mandatory` cannot be turned off
/// (R3).</summary>
public static class NotificationKinds
{
    public sealed record Kind(string Key, string Group, string Label, bool Mandatory = false);

    public static readonly Kind[] All =
    [
        new("job.failed", "Job", "Job failed"),
        new("job.succeeded", "Job", "Job finished (ran > 10 minutes)"),
        new("run.finished", "Job", "Training run finished"),
        new("run.failed", "Job", "Training run failed"),
        new("notebook.idle_warning", "Job", "Notebook about to stop automatically"),
        new("job.signal_lost", "Job", "Job lost signal"),
        new("job.stalled", "Job", "Job may be stalled"),
        new("sweep.completed", "Job", "HPO sweep finished"),
        new("sweep.failed", "Job", "HPO sweep failed"),
        new("pipeline.succeeded", "Job", "Pipeline run finished"),
        new("pipeline.failed", "Job", "Pipeline failed"),
        new("labeling.assigned", "Labeling", "Item assigned"),
        new("labeling.rejected", "Labeling", "Item rejected"),
        new("labeling.project_completed", "Labeling", "Labeling project completed"),
        new("model.promoted", "Model", "Model promoted to production"),
        new("model.evaluated", "Model", "Model evaluation finished"),
        new("deployment.degraded", "Model", "Deployment degraded"),
        new("deployment.failed", "Model", "Deployment failed"),
        new("deployment.drift", "Model", "Deployment data drift"),
        new("fleet.host_offline", "Machines", "Machine offline"),
        new("audit.integrity_broken", "System", "Audit chain broken", Mandatory: true),
    ];

    public static Kind? Find(string key) => All.FirstOrDefault(k => k.Key == key);
}
