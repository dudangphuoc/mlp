using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>Display name of a user, for modules that only hold the user id.</summary>
public sealed record UserNameRow(Guid Id, string DisplayName);
