using MLPlatform.CodeGate.Architecture;

namespace MLPlatform.CodeGate.Rules;

/// <summary>MLPARCH001 a module depends on a higher layer or an undeclared same-layer module; MLPARCH002 a module calls another module's SQL.</summary>
public static class ArchitectureRules
{
    public static IEnumerable<Finding> Check(RepoFiles repo)
    {
        var manifest = ModuleManifest.Load(repo);
        var references = ModuleGraph.Build(repo, manifest);
        foreach (var edge in references.Where(r => !manifest.Allows(r.From, r.To)).Select(r => (r.From, r.To)).Distinct())
            yield return new("MLPARCH001", $"{edge.From}->{edge.To}", $"{edge.From} -> {edge.To}", "dependency goes up a layer or is not declared in modules.json");
        foreach (var call in references.Where(r => r.Kind == "sql" && !manifest.Exposes(r.To, r.Target)).Select(r => (r.From, r.To, r.Target)).Distinct())
            yield return new("MLPARCH002", $"{call.From}->{call.To}.{call.Target}", $"{call.From} -> {call.To}", $"calls {call.To} SQL method {call.Target}");
    }
}
