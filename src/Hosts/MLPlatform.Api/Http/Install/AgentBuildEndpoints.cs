using MLPlatform.ControlPlane.Infrastructure;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Infrastructure;

/// <summary>Plug-and-play install endpoint (script sends doctor → per-host build → signed download).</summary>
public static class AgentBuildEndpoints
{
    public static void MapAgentBuilds(this WebApplication app)
    {
        var store = app.Services.GetRequiredService<AgentBuildStore>();

        async Task<bool> Authorized(HttpContext ctx)
        {
            var h = ctx.Request.Headers.Authorization.ToString();
            var token = h.StartsWith("Bearer ", StringComparison.Ordinal) ? h[7..] : "";
            return await ctx.RequestServices.GetRequiredService<IMediator>().Query(new EnrollTokenValidQuery(token), ctx.RequestAborted);
        }

        app.MapPost("/install/agent/build", async (HttpContext ctx) =>
        {
            if (!await Authorized(ctx)) return Results.Text("The install command has expired or was already used", statusCode: 401);
            if (await JsonNode.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted) is not JsonObject doctor)
                return Results.Text("Missing doctor result", statusCode: 400);
            if (AgentBuildStore.Rid(doctor) is not { } rid) return Results.Text($"Platform not supported yet: {doctor["os"]}/{doctor["arch"]}", statusCode: 422);
            if (store.Version is not { } ver) return Results.Text("The control plane has no agent-builder yet (missing source-version)", statusCode: 503);
            return Results.Text(store.Request(ver, rid, doctor, doctor["retry"]?.GetValue<bool>() == true).ToJsonString(), "application/json");
        });
        app.MapGet("/install/agent/build/{rid}", (string rid) =>
            !AgentBuildStore.Supported.Contains(rid) ? Results.NotFound()
            : store.Version is { } ver ? Results.Text(store.Status(ver, rid).ToJsonString(), "application/json") : Results.StatusCode(503));
        // Binary download: requires a valid, unexpired HMAC signature (link issued by /install/agent/build or OTA).
        app.MapGet("/install/agent/{ver}/{rid}/{file}", (string ver, string rid, string file, long? e, string? s) =>
        {
            if (!(AgentBuildStore.Supported.Contains(rid) || AgentBuildStore.ShimRids.Contains(rid)) || file != AgentBuildStore.Exe(rid) || ver.Any(ch => !char.IsAsciiLetterOrDigit(ch))) return Results.NotFound();
            if (e is null || !store.Verify(ver, rid, file, e.Value, s ?? "")) return Results.StatusCode(403);
            var path = store.CachePath(ver, rid, file);
            return File.Exists(path) ? Results.File(path, "application/octet-stream", file) : Results.NotFound();
        });
    }
}
