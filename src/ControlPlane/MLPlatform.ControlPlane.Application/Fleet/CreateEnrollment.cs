using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

// ---- Create the machine plug-in command (M02 §5.1) ----
[RequiresPermission("fleet.manage")]
public sealed record CreateEnrollmentCommand(Guid WorkspaceId, string Os, string DataPath, string ModelsPath) : ICommand<EnrollmentDto>;

public sealed class CreateEnrollmentCommandHandler(IWriteSession db, IClock clock, TokenService tokens, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateEnrollmentCommand, EnrollmentDto>
{
    public async ValueTask<EnrollmentDto> Handle(CreateEnrollmentCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "fleet.manage", ct);
        if (string.IsNullOrWhiteSpace(c.DataPath) || string.IsNullOrWhiteSpace(c.ModelsPath))
            throw new ValidationException(new Dictionary<string, string> { ["data_path"] = "data_path and models_path are required" });
        var token = TokenService.New("mlpe_");
        var now = clock.UtcNow;
        var id = Ids.New();
        await db.InsertEnrollmentAsync(id, c.WorkspaceId, tokens.Hash(token), c.Os, c.DataPath, c.ModelsPath, uid, now,
            now.AddMinutes(10), ct);
        return new EnrollmentDto(id, token, now.AddMinutes(10), c.WorkspaceId);
    }
}
