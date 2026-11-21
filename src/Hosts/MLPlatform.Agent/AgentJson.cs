using System.Text.Json;
using System.Text.Json.Serialization;

namespace MLPlatform.Agent;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AgentOptions))]
internal sealed partial class AgentJson : JsonSerializerContext;
