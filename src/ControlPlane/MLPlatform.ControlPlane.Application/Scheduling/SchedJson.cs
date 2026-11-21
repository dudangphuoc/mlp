using System.Text.Json;
using System.Text.Json.Serialization;

namespace MLPlatform.ControlPlane.Application.Scheduling;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(JobSpecDto))]
[JsonSerializable(typeof(List<BundleDto>))]
[JsonSerializable(typeof(List<HeldBundleDto>))]
public sealed partial class SchedJson : JsonSerializerContext;
