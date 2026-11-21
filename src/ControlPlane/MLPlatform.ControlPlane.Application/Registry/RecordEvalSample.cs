using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

public sealed record RecordEvalSampleCommand(Guid EvaluationId, int Idx, string Path, int Width, int Height, string Gt, string Pred, int Tp, int Fp, int Fn,
    bool Correct, string? Error, double LatencyMs) : ICommand<bool>, IInternalCommand;

public sealed class RecordEvalSampleCommandHandler(IWriteSession db) : ICommandHandler<RecordEvalSampleCommand, bool>
{
    public async ValueTask<bool> Handle(RecordEvalSampleCommand c, CancellationToken ct)
    {
        await db.InsertEvalSampleAsync(c.EvaluationId, c.Idx, c.Path, c.Width, c.Height, c.Gt, c.Pred, c.Tp, c.Fp, c.Fn, c.Correct, c.Error, c.LatencyMs, ct);
        return true;
    }
}
