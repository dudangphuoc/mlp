using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>Hands a queued import to the worker in the api host (the pipeline engine starts imports too).</summary>
public interface IHubImportQueue
{
    void Enqueue(Guid importId);
}
