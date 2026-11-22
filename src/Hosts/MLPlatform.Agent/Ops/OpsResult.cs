using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Ops;

/// <summary>Shared failure results for remote ops commands.</summary>
public static class OpsResult
{
    public static CommandResult Fail(string reason, string message) => new() { Ok = false, ErrorReason = reason, Message = message };

    public static CommandResult Outside() => Fail("FLEET_PATH_OUTSIDE_ROOT", "Path is outside the allowed folders");

    public static CommandResult NotFound() => Fail("FLEET_PATH_NOT_FOUND", "Path does not exist");
}
