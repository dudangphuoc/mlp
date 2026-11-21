using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A trial as the canvas shows it: stored row, live stages while its job runs, the job and the machine.</summary>
public sealed record TrialView(TrialRow Row, string State, IReadOnlyList<TrialStageView> Stages, double? StepsPerS, long? PeakVramMb, bool VramEstimated,
    int TrainImages, int ValImages, JobInfoRow? Job, string? HostName);
