using MLPlatform.CodeGate;
using MLPlatform.CodeGate.Reports;
using MLPlatform.CodeGate.Rules;

var options = GateOptions.Parse(args);
var repo = new RepoFiles(options.Root);

if (options.MetricsOutput is { } metricsPath)
{
    MetricsReport.Write(repo, metricsPath);
    Console.WriteLine($"metrics written to {metricsPath}");
    return 0;
}

if (options.CommentInventoryOutput is { } inventoryPath)
{
    CommentInventory.Write(repo, inventoryPath);
    Console.WriteLine($"comment inventory written to {inventoryPath}");
    return 0;
}

List<Finding> findings = [.. MigrationRules.Check(repo), .. SqlRules.Check(repo), .. CommentRules.Check(repo)];
var baseline = new Baseline(Path.Combine(options.Root, "gates", "baseline.txt"));

if (options.UpdateBaseline)
{
    baseline.Save(findings.Where(f => RuleSet.IsEnforced(f.Rule)));
    Console.WriteLine($"baseline written with {findings.Count(f => RuleSet.IsEnforced(f.Rule))} entries");
    return 0;
}

return GateResult.Report(findings, baseline);
