using MLPlatform.Data;

namespace MLPlatform.Api.Commands;

/// <summary>`mlp-api db apply --root &lt;db&gt;`: applies pending migrations, then exits.</summary>
public static class DbApplyCommand
{
    public static bool IsRequested(string[] args) => args is ["db", "apply", ..];

    public static async Task RunAsync(string[] args)
    {
        var root = args.SkipWhile(a => a != "--root").Skip(1).FirstOrDefault() ?? "db";
        var cs = Environment.GetEnvironmentVariable("MLP__Db__ConnectionString")
                 ?? throw new InvalidOperationException("Missing MLP__Db__ConnectionString");
        await using var ds = new Npgsql.NpgsqlSlimDataSourceBuilder(cs).Build();
        var applied = await new ScriptRunner(ds, Console.Out).ApplyAsync(ScriptRunner.Discover(root), CancellationToken.None);
        Console.WriteLine($"done: {applied} new migration(s)");
    }
}
