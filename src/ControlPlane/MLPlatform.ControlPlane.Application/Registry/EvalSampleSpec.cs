using System.Globalization;
using System.Text.Json.Nodes;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Ground truth of one sample, read from the dataset version (no labels → negative image for
/// detection).</summary>
public sealed record EvalSampleSpec(string ImagePath, string? LabelPath, string? ClassName);
