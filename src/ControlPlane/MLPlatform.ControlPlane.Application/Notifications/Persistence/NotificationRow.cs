using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

public sealed record NotificationRow(Guid Id, string Kind, string Title, string Link, string ResourceUri, string Severity, int Count,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ReadAt);
