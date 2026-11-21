using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>An email waiting to be sent.</summary>
public sealed record EmailRow(Guid Id, string ToEmail, string Subject, string Body, string Kind, int Attempts);
