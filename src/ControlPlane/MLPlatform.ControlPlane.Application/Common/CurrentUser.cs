namespace MLPlatform.ControlPlane.Application.Common;

/// <summary>Scoped principal: set by the auth middleware per request, or by a background loop acting for the user who
/// started the work.</summary>
public sealed class CurrentUser : ICurrentUser
{
    public Guid? UserId { get; private set; }

    public string? SessionTokenHash { get; private set; }

    public void SignIn(Guid userId, string sessionTokenHash) => (UserId, SessionTokenHash) = (userId, sessionTokenHash);

    public void ActAs(Guid userId) => UserId = userId;
}
