namespace MLPlatform.CodeGate;

public static class GateResult
{
    public static int Report(IReadOnlyList<Finding> findings, Baseline baseline)
    {
        var enforced = findings.Where(f => RuleSet.IsEnforced(f.Rule)).ToList();
        var fresh = enforced.Where(f => !baseline.Entries.Contains(f.BaselineEntry)).ToList();
        var current = enforced.Select(f => f.BaselineEntry).ToHashSet();
        var fixedCount = baseline.Entries.Count(e => !current.Contains(e));
        foreach (var f in fresh) Console.WriteLine($"FAIL {f.Rule} {f.Location}: {f.Message}");
        foreach (var group in findings.Where(f => !RuleSet.IsEnforced(f.Rule)).GroupBy(f => f.Rule).OrderBy(g => g.Key))
            Console.WriteLine($"report {group.Key}: {group.Count()} (not enforced yet)");
        Console.WriteLine($"gate: {enforced.Count} enforced violations ({fresh.Count} new, {enforced.Count - fresh.Count} in baseline, {fixedCount} fixed)");
        return fresh.Count == 0 ? 0 : 1;
    }
}
