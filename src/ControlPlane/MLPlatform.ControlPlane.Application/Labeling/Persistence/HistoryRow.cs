using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record HistoryRow(string Action, string Username, DateTimeOffset At);
