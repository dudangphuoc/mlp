using System.Text;
using System.Text.Json.Nodes;

namespace MLPlatform.Recipes;

/// <summary>Writes a recipe's Dockerfile from its manifest: no network access in the build except pip and apt; the
/// source code arrives as archives the agent downloads, models are fetched when the job runs.</summary>
public static class RecipeDockerfile
{
    public const string GitScheme = "git:";
    public const string DefaultSourcePath = "/opt/recipe/src";

    /// <summary>True when the manifest describes its environment (environment.base).</summary>
    public static bool Applies(JsonObject manifest) => manifest["environment"]?["base"] is JsonValue;

    /// <summary>The Dockerfile text; adds the source archives and upstream files to fetches.</summary>
    public static string Generate(JsonObject manifest, string recipe, bool hasConstraints, List<RecipeFetch> fetches)
    {
        var env = manifest["environment"]!.AsObject();
        var src = manifest["source"] as JsonObject ?? [];
        var sb = new StringBuilder();
        sb.Append("FROM ").Append(Str(env, "base")).Append('\n');
        sb.Append("ENV PIP_BREAK_SYSTEM_PACKAGES=1 PYTHONUNBUFFERED=1");
        foreach (var (k, v) in env["env"] as JsonObject ?? []) sb.Append(' ').Append(k).Append('=').Append(Quote(v?.ToString() ?? ""));
        sb.Append('\n');
        if (List(env["apt"]) is { Count: > 0 } apt)
            sb.Append("RUN apt-get update && apt-get install -y --no-install-recommends ").Append(string.Join(' ', apt))
              .Append(" && rm -rf /var/lib/apt/lists/*\n");
        sb.Append("RUN command -v python >/dev/null || ln -s \"$(command -v python3)\" /usr/local/bin/python\n");
        if (hasConstraints)
            sb.Append("COPY ").Append(recipe).Append("/constraints.txt /opt/recipe/constraints.txt\nENV PIP_CONSTRAINT=/opt/recipe/constraints.txt\n");
        AppendRepos(sb, src, fetches);
        if ((env["pip"] is null ? List(src["pip"]) : List(env["pip"])) is { Count: > 0 } pip)
            sb.Append("RUN --mount=type=cache,target=/root/.cache/pip,sharing=locked python -m pip install ")
              .Append(string.Join(' ', pip.Select(Arg))).Append('\n');
        AppendFiles(sb, src, fetches);
        foreach (var c in (env["copy"] as JsonArray)?.OfType<JsonObject>() ?? [])
            sb.Append("COPY ").Append(Str(c, "from")).Append(' ').Append(Str(c, "to")).Append('\n');
        foreach (var line in List(env["setup"])) sb.Append("RUN ").Append(line).Append('\n');
        if (Str(env, "check") is { Length: > 0 } check) sb.Append("RUN ").Append(check).Append('\n');
        sb.Append("WORKDIR ").Append(Str(env, "workdir") is { Length: > 0 } w ? w : "/opt/recipe").Append('\n');
        return sb.ToString();
    }

    private static void AppendRepos(StringBuilder sb, JsonObject src, List<RecipeFetch> fetches)
    {
        var repos = new List<(string Git, string Commit, string Path)>();
        if (Str(src, "git") is { Length: > 0 } git) repos.Add((git, Str(src, "commit"), Str(src, "path") is { Length: > 0 } p ? p : DefaultSourcePath));
        foreach (var r in (src["extraRepos"] as JsonArray)?.OfType<JsonObject>() ?? []) repos.Add((Str(r, "git"), Str(r, "commit"), Str(r, "path")));
        for (var i = 0; i < repos.Count; i++)
        {
            var name = $"_src/repo-{i}.tar.gz";
            fetches.Add(new RecipeFetch(name, $"{GitScheme}{repos[i].Git}@{repos[i].Commit}", ""));
            sb.Append("ADD ").Append(name).Append(' ').Append(repos[i].Path.TrimEnd('/')).Append("/\n");
        }
    }

    private static void AppendFiles(StringBuilder sb, JsonObject src, List<RecipeFetch> fetches)
    {
        var files = (src["files"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        for (var i = 0; i < files.Count; i++)
        {
            var name = $"_src/file-{i}";
            fetches.Add(new RecipeFetch(name, Str(files[i], "url"), Str(files[i], "sha256")));
            sb.Append("COPY ").Append(name).Append(' ').Append(Str(files[i], "path")).Append('\n');
        }
    }

    private static string Arg(string a) => a.StartsWith('-') ? a : Quote(a);

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    private static string Str(JsonNode? n, string k) => n?[k]?.ToString() ?? "";

    private static List<string> List(JsonNode? n) => (n as JsonArray)?.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList() ?? [];
}
