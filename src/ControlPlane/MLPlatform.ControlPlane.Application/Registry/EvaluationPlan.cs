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

public sealed record EvaluationPlan(EvaluationRow Evaluation, string Task, IReadOnlyList<string> Classes, IReadOnlyList<(int Idx, string Image, string ImageUrl,
    string? Label, string? LabelUrl, string? ClassName)> Samples, string? CocoUrl, ISet<int> Done);
