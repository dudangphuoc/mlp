using MLPlatform.ControlPlane.Infrastructure;

namespace MLPlatform.Api.Http.Install;

/// <summary>Agent install scripts with the public URL, agent address and certificate pin filled in.</summary>
public static class InstallScriptEndpoints
{
    public static void MapInstallScripts(this IEndpointRouteBuilder app, IConfiguration cfg)
    {
        app.MapGet("/install/agent.sh", () => Script(cfg, ShellScriptPath(cfg), "text/x-shellscript"));
        app.MapGet("/install/agent.ps1", () => Script(cfg, Path.Combine(Path.GetDirectoryName(ShellScriptPath(cfg))!, "agent.ps1"), "text/plain; charset=utf-8"));
    }

    private static string ShellScriptPath(IConfiguration cfg) =>
        cfg["Install:ScriptPath"] ?? Path.Combine(AppContext.BaseDirectory, "install", "agent.sh");

    private static IResult Script(IConfiguration cfg, string path, string contentType)
    {
        if (!File.Exists(path)) return Results.NotFound();
        var text = File.ReadAllText(path)
            .Replace("__MLP_PUBLIC_URL__", cfg["PublicUrl"] ?? "")
            .Replace("__MLP_AGENT_ADDR__", cfg["Install:AgentAddress"] ?? "")
            .Replace("__MLP_AGENT_PIN__", AgentListener.CertPin(cfg) ?? "");
        return Results.Text(text, contentType);
    }
}
