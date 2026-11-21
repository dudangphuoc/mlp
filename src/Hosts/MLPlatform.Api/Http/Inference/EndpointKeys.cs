using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Http.Inference;

/// <summary>Authenticates `Authorization: Bearer mlpd_…` against a deployment and reads the JSON body.</summary>
public static class EndpointKeys
{
    private const string Scheme = "Bearer ";

    public static async Task<EndpointResolution> ResolveAsync(Guid workspaceId, string slug, HttpContext http, IMediator mediator, bool requireHealthy)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(Scheme + "mlpd_", StringComparison.Ordinal)) return PredictionJson.Error(401, "SERVE_KEY_INVALID", "Missing endpoint key");
        try { return await mediator.Query(new ResolveEndpointQuery(header[Scheme.Length..], workspaceId, slug, RequireHealthy: requireHealthy), http.RequestAborted); }
        catch (UnauthenticatedException e) { return PredictionJson.Error(401, e.Reason, e.Message); }
        catch (NotFoundException e) { return PredictionJson.Error(404, e.Reason, e.Message); }
        catch (UnavailableException e) { return PredictionJson.Error(503, e.Reason, e.Message); }
    }

    public static async Task<JsonNode?> ReadBodyAsync(HttpContext http)
    {
        try { return await JsonNode.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted) ?? new JsonObject(); }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
