namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>F5 debug of a remote code run: the IDE adapter's token, the developer's process id, the repository root on
/// the developer's machine.</summary>
public sealed record RemoteDebug(string Token, int ParentPid, string Root);
