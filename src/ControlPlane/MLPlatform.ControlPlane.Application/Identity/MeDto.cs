using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

public sealed record MeDto(Guid UserId, string Email, string Username, string DisplayName, bool IsInstanceAdmin,
    IReadOnlyList<MembershipRow> Workspaces);
