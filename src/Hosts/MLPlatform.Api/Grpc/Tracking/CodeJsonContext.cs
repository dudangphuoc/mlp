namespace MLPlatform.Api.Grpc;

/// <summary>AOT serializer for <see cref="CodeJson"/>.</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(CodeJson))]
internal sealed partial class CodeJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
