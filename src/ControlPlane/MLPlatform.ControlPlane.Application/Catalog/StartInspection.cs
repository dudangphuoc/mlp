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

/// <summary>Internal: marks an inspection as running (a previous result for the same key is cleared) and returns the row.</summary>
public sealed record StartInspectionCommand(InspectionKey Key, string RecipeName) : ICommand<InspectionRow>;

/// <summary>One row per key; starting again resets it.</summary>
public sealed class StartInspectionCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<StartInspectionCommand, InspectionRow>
{
    public async ValueTask<InspectionRow> Handle(StartInspectionCommand c, CancellationToken ct)
    {
        var k = c.Key;
        await db.StartInspectionAsync(Ids.New(), k.WorkspaceId, c.RecipeName, k.EnvTag, k.Variant, k.ParamsSha256, k.NumClasses, clock.UtcNow, ct);
        return (await db.GetInspectionAsync(k.WorkspaceId, k.EnvTag, k.Variant, k.ParamsSha256, k.NumClasses, ct))!;
    }
}
