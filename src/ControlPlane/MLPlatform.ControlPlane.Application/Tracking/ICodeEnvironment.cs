namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Fingerprint of what the platform adds to every code environment image (SDK, Dockerfile).</summary>
public interface ICodeEnvironment
{
    string Fingerprint { get; }
}
