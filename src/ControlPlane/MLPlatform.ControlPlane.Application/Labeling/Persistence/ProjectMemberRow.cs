using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record ProjectMemberRow(Guid UserId, string Username, string Role, int MaxOpenItems, long OpenItems);
