namespace MLPlatform.ControlPlane.Application.Timeline;

public sealed record EventRow(Guid Id, DateTimeOffset At, string Source, string ResourceType, Guid? ResourceId, string Kind, string Severity, string Message, string Data);
