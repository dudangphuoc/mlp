namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Reacts when a machine goes offline (recovery marks its running attempts suspect).</summary>
public interface IHostLossListener
{
    Task OnHostOfflineAsync(Guid hostId, string hostName, CancellationToken ct);
}
