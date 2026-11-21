using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Common;

/// <summary>Principal of the current request (filled by Hosting from cookie/Bearer).</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? SessionTokenHash { get; }
}
