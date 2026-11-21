using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

public static class NotebookRules
{
    public const int ContainerPort = 8888;
    public const string HomeMount = "/home/mlp";
    public const int MaxActivePerUser = 3;   // temporary until sched.quotas exists (R7)

    /// <summary>Jupyter token derived from the server key — not stored in the DB; the api attaches it when proxying (§5 step 2).</summary>
    public static string JupyterToken(TokenService tokens, Guid serverId) => tokens.Hash("nb-jupyter:" + serverId);

    public static string BaseUrl(Guid serverId) => $"/nb/{serverId}/";

    /// <summary>JupyterLab listens only inside the container; the host port is bound to 127.0.0.1 only (R2).</summary>
    public static List<string> Command(string ide, Guid serverId, string token) => ide switch
    {
        "jupyterlab" =>
        [
            "jupyter", "lab", "--ip=0.0.0.0", $"--port={ContainerPort}", "--no-browser", $"--ServerApp.base_url={BaseUrl(serverId)}",
            $"--IdentityProvider.token={token}", "--ServerApp.allow_remote_access=True", "--ServerApp.allow_origin=*", $"--ServerApp.root_dir={HomeMount}",
        ],
        _ => throw new InvalidArgumentException("NB_IDE_UNSUPPORTED", "IDE not supported yet (available: jupyterlab)"),
    };
}
