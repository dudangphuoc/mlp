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

/// <summary>Internal: forgets an inspection that could not run because the image is gone from the machine.</summary>
public sealed record DeleteInspectionCommand(InspectionKey Key) : ICommand<bool>;

/// <summary>Removes the row so the next request starts from the environment again.</summary>
public sealed class DeleteInspectionCommandHandler(IWriteSession db) : ICommandHandler<DeleteInspectionCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteInspectionCommand c, CancellationToken ct)
    {
        var k = c.Key;
        await db.DeleteInspectionAsync(k.WorkspaceId, k.EnvTag, k.Variant, k.ParamsSha256, k.NumClasses, ct);
        return true;
    }
}
