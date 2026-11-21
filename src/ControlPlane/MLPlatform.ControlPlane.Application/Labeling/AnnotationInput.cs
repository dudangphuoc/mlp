using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Label submitted from the labeling tool (M05 §3.1: image coordinates normalized to 0–1).</summary>
public sealed record AnnotationInput(string Kind, int ClassId, string Geometry, string Attributes, string Source, double? Confidence);
