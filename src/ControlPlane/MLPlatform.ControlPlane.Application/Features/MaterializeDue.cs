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

/// <summary>Background loop: materializes views whose schedule is due.</summary>
public sealed record MaterializeDueCommand : ICommand<int>, IInternalCommand;

public sealed class MaterializeDueCommandHandler(IWriteSession db, IClock clock, S3Client s3) : ICommandHandler<MaterializeDueCommand, int>
{
    public async ValueTask<int> Handle(MaterializeDueCommand c, CancellationToken ct)
    {
        var due = await db.DueViewsAsync(clock.UtcNow, ct);
        foreach (var v in due) await Materializer.RunAsync(db, s3, clock, v, ct);
        return due.Count;
    }
}
