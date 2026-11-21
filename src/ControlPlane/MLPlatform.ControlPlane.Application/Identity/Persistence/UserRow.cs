using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Identity;

public sealed record UserRow(Guid Id, string Email, string Username, string DisplayName, string PasswordHash,
    UserStatus Status, bool IsInstanceAdmin);
