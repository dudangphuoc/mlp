using System.Text.Json.Nodes;
using MLPlatform.Recipes;
using MLPlatform.Shim;

// mlp-shim run --spec <job.json>          : run one attempt (entrypoint of every workload container)
// mlp-shim selfcheck <manifest> <tasks>   : M17 R2a
if (args is ["selfcheck", var manifestPath, var tasksPath])
{
    var known = (YamlJson.Parse(File.ReadAllText(tasksPath))?["tasks"] as JsonArray)?
        .Select(t => t!["key"]!.GetValue<string>()).ToHashSet();
    if (known is null) { Console.Error.WriteLine($"{tasksPath}: not a task catalog (missing `tasks`)"); return 2; }
    if (YamlJson.Parse(File.ReadAllText(manifestPath)) is not JsonObject manifest) { Console.Error.WriteLine($"{manifestPath}: manifest is empty"); return 2; }
    var errs = RecipeChecker.Check(manifest, known);
    foreach (var e in errs) Console.Error.WriteLine(e);
    return errs.Count == 0 ? 0 : 3;
}
if (args is ["run", "--spec" or "--spec-env", var specPath])
{
    var cts = new CancellationTokenSource();   // not disposed: ProcessExit/SIGTERM may arrive after Run finishes
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, c => { c.Cancel = true; cts.Cancel(); });
    try
    {
        var spec = args[1] == "--spec-env" ? JobSpec.FromEnvironment(specPath) : JobSpec.Load(specPath);
        Directory.CreateDirectory(spec.Output);
        return await new Runner(spec, Console.Error).RunAsync(cts.Token);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        // SIGTERM (stop/cancel/agent relocation): exit like a TERM-ed process → the scheduler treats it as an infrastructure error (retry from checkpoint).
        Console.Error.WriteLine("[mlp-shim] received SIGTERM; stopping");
        return 143;
    }
    catch (ShimException e)
    {
        if (e.ExitCode == 1) Console.Error.WriteLine("[mlp-shim] " + e.Message);
        Console.Error.WriteLine("MLP_ERROR " + new JsonObject { ["reason"] = e.Reason, ["message"] = e.Message }.ToJsonString());
        return e.ExitCode;
    }
}
Console.Error.WriteLine("Usage: mlp-shim run --spec <job.json> | mlp-shim selfcheck <manifest> <tasks.yaml>");
return 2;
