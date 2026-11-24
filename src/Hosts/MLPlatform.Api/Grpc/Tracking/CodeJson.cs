namespace MLPlatform.Api.Grpc;

/// <summary>Stored shape of a code run's origin (track.runs.code).</summary>
public sealed record CodeJson(
    [property: System.Text.Json.Serialization.JsonPropertyName("gitRemote")] string? GitRemote,
    [property: System.Text.Json.Serialization.JsonPropertyName("commit")] string? Commit,
    [property: System.Text.Json.Serialization.JsonPropertyName("dirty")] bool Dirty,
    [property: System.Text.Json.Serialization.JsonPropertyName("script")] string? Script,
    [property: System.Text.Json.Serialization.JsonPropertyName("argv")] List<string>? Argv,
    [property: System.Text.Json.Serialization.JsonPropertyName("python")] string? Python,
    [property: System.Text.Json.Serialization.JsonPropertyName("branch")] string? Branch);
