using System.Text.Json.Nodes;
using MLPlatform.Recipes;

if (args is ["recipe", "check", var dir])
{
    var tasksDoc = YamlJson.Parse(File.ReadAllText(Path.Combine(dir, "_tasks", "tasks.yaml")))!;
    var known = tasksDoc["tasks"]!.AsArray().Select(t => t!["key"]!.GetValue<string>()).ToHashSet();
    var errors = 0;
    foreach (var f in Directory.GetFiles(dir, "mlp-recipe.yaml", SearchOption.AllDirectories).Order())
    {
        var m = YamlJson.Parse(File.ReadAllText(f)) as JsonObject;
        var errs = m is null ? [$"{f}: empty"] : RecipeChecker.Check(m, known);
        Console.WriteLine($"{(errs.Count == 0 ? "OK  " : "FAIL")} {Path.GetFileName(Path.GetDirectoryName(f))}");
        foreach (var e in errs) Console.WriteLine("     " + e);
        errors += errs.Count;
    }
    return errors == 0 ? 0 : 1;
}
if (args is ["recipe", "dockerfile", var recipeDir])
{
    var name = Path.GetFileName(Path.GetFullPath(recipeDir).TrimEnd('/'));
    if (YamlJson.Parse(File.ReadAllText(Path.Combine(recipeDir, "mlp-recipe.yaml"))) is not JsonObject m || !RecipeDockerfile.Applies(m))
    {
        Console.Error.WriteLine("The manifest has no environment.base");
        return 1;
    }
    var fetches = new List<RecipeFetch>();
    Console.Write(RecipeDockerfile.Generate(m, name, File.Exists(Path.Combine(recipeDir, "constraints.txt")), fetches));
    foreach (var f in fetches) Console.WriteLine($"# fetch {f.Path} ← {f.Url} {f.Sha256}");
    return 0;
}
if (args is ["s3", "smoke", var ep, var ak, var sk])
{
    using var http = new HttpClient();
    var s3 = new MLPlatform.Storage.S3Client(http, new MLPlatform.Storage.S3Options(ep, ak, sk), TimeProvider.System);
    var ct = CancellationToken.None;
    await s3.EnsureBucketAsync("mlp-smoke", ct);
    await s3.EnsureBucketAsync("mlp-smoke", ct);
    await s3.PutAsync("mlp-smoke", "a/b c/x.txt", "hello ✓"u8.ToArray(), "text/plain", ct);
    var head = await s3.HeadAsync("mlp-smoke", "a/b c/x.txt", ct);
    var list = await s3.ListAsync("mlp-smoke", "a/", ct);
    var got = System.Text.Encoding.UTF8.GetString((await s3.GetAsync("mlp-smoke", "a/b c/x.txt", ct))!);
    Console.WriteLine($"head={head?.Size} list={list.Count} get={got}");
    Console.WriteLine("PUTURL " + s3.Presign(HttpMethod.Put, "mlp-smoke", "up/file.bin", TimeSpan.FromMinutes(5)));
    Console.WriteLine("GETURL " + s3.Presign(HttpMethod.Get, "mlp-smoke", "up/file.bin", TimeSpan.FromMinutes(5)));
    return head?.Size == System.Text.Encoding.UTF8.GetByteCount("hello ✓") && list.Count == 1 && got == "hello ✓" ? 0 : 1;
}
Console.Error.WriteLine("Usage: mlp recipe check <dir> | mlp s3 smoke <endpoint> <access> <secret>");
return 2;
