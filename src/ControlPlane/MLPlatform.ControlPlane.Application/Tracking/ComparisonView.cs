using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Round 1 §8.6: can the numbers be compared (same validation set, same metric axis), and only the lines that
/// differ.</summary>
public sealed record ComparisonView(bool SameVal, bool SameAxis, IReadOnlyList<string> Axes, IReadOnlyList<CompareRow> Rows);
