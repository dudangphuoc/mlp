using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Platform;

/// <summary>One dynamic setting (config = JSON of plain fields; secrets_enc = JSON of secret fields encrypted by the
/// api).</summary>
public sealed record SettingRow(string Key, bool Enabled, string ConfigJson, string? SecretsEnc, Guid? UpdatedBy, DateTimeOffset UpdatedAt);
