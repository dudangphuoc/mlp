using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

public static class Phases
{
    public static string Label(string phase) => phase switch
    {
        "preparing_data" => "Preparing data",
        "restoring_checkpoint" => "Restoring checkpoint",
        "running" => "Running",
        "uploading" => "Uploading results",
        "finishing" => "Finishing",
        _ => phase,
    };
}
