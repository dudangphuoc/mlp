using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

internal static class NotebookAccess
{
    public static async Task<ServerRow> LoadAsync(IReadSession db, WorkspaceAuthorizer auth, Guid ws, Guid id, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(ws, "notebook.use", ct);
        var s = await db.GetServerAsync(id, ws, ct);
        if (s is null || (s.UserId != uid && !await auth.HasAsync(ws, "notebook.admin", ct)))
            throw new NotFoundException("NB_NOT_FOUND", "Notebook not found");
        return s;
    }
}
