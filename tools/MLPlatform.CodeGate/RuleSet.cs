namespace MLPlatform.CodeGate;

/// <summary>Rules that fail the gate; the others are reported only until their refactor phase turns them on.</summary>
public static class RuleSet
{
    private static readonly HashSet<string> Enforced = ["MLPMIG001", "MLPSQL001", "MLPSQL010", "MLPSQL020", "MLPARCH001", "MLPARCH002", "MLPAPI001", "MLPCODE002", "MLPCODE001", "MLPCODE003", "MLPCODE004"];

    public static bool IsEnforced(string rule) => Enforced.Contains(rule);
}
