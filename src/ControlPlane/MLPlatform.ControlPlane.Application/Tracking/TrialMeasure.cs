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

/// <summary>What the shim measured during stage 1 (the last "[mlp-trial] {"key":"measure",…}" line).</summary>
public sealed record TrialMeasure(double? StepsPerS, long? PeakVramMb, bool VramEstimated, double? ValSPerBatch, int? TrainImages, int? ValImages);
