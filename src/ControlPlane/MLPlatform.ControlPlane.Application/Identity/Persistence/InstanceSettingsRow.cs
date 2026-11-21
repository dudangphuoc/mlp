using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Identity;

public sealed record InstanceSettingsRow(string Name, bool AllowSignup, string? BootstrapTokenHash,
    DateTimeOffset? BootstrapExpiresAt, DateTimeOffset? BootstrappedAt);
