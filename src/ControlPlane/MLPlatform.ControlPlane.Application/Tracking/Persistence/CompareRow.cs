using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>One line that differs between the compared runs (or revisions): field, canvas pane, one shown value per
/// item.</summary>
public sealed record CompareRow(string Field, string Pane, IReadOnlyList<string> Values);
