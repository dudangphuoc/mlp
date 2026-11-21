using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Detection thresholds (20/10 §10): shim silent → signal lost; silent much longer → stop and retry.</summary>
public sealed record SignalOptions(TimeSpan SignalLostAfter, TimeSpan SignalKillAfter);
