using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Features;

public sealed record MaterializeViewCommand(Guid WorkspaceId, Guid ViewId) : ICommand<ViewRow>;

public sealed class MaterializeViewCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, S3Client s3) : ICommandHandler<MaterializeViewCommand, ViewRow>
{
    public async ValueTask<ViewRow> Handle(MaterializeViewCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        var v = await db.GetViewAsync(c.ViewId, c.WorkspaceId, ct) ?? throw new NotFoundException("FEATURE_VIEW_NOT_FOUND", "Feature view not found");
        return await Materializer.RunAsync(db, s3, clock, v, ct);
    }
}
