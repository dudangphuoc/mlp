using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Common;

public static class CurrentUserExtensions
{
    public static Guid RequireUser(this ICurrentUser u) =>
        u.UserId ?? throw new UnauthenticatedException("IAM_UNAUTHENTICATED", "Sign-in required");
}
