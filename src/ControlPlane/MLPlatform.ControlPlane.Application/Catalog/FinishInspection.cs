using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

/// <summary>Internal: stores what the inspection container reported (ready with its result, or failed with the reason).</summary>
public sealed record FinishInspectionCommand(InspectionKey Key, InspectionOutput Output) : ICommand<bool>;

/// <summary>Writes the result onto the running row.</summary>
public sealed class FinishInspectionCommandHandler(IWriteSession db) : ICommandHandler<FinishInspectionCommand, bool>
{
    public async ValueTask<bool> Handle(FinishInspectionCommand c, CancellationToken ct)
    {
        var (k, o) = (c.Key, c.Output);
        await db.FinishInspectionAsync(k.WorkspaceId, k.EnvTag, k.Variant, k.ParamsSha256, k.NumClasses, o.Ok ? "ready" : "failed", o.Error,
            o.OverlayYaml, o.ConfigJson, o.BlocksJson, o.SourcesJson, ct);
        return true;
    }
}
