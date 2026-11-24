namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A code run about to be inserted.</summary>
public sealed record NewCodeRun(Guid Id, Guid WorkspaceId, ProjectRow Project, string Name, Guid UserId, string ParamsJson, int Number, string CodeJson);
