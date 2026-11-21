using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Identity;

public sealed record MembershipRow(Guid Id, string Slug, string Name, string Kind, DateTimeOffset CreatedAt, string Role);
