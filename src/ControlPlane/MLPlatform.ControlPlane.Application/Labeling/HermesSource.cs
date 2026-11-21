using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Where the item's content lives (read by the api to give text to the decision model).</summary>
public sealed record HermesSource(string? Bucket, string? Key, string? ExternalUrl);
