using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

public static class RunArtifacts
{
    public static string Prefix(Guid ws, Guid run) => $"{ws}/runs/{run}/model/";
}
